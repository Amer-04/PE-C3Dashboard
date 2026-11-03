import { TestBed } from '@angular/core/testing';
import { SiteSuitability, SuitabilityItem } from '../../core/models';
import { SiteSummaryComponent } from './site-summary.component';

const item = (key: string, answer: SuitabilityItem['answer'], basis: SuitabilityItem['basis'], headline: string, nextStep: string | null = null): SuitabilityItem =>
  ({ key, question: `${key}?`, answer, basis, headline, detail: `${key} detail`, nextStep });

const summary: SiteSuitability = {
  items: [
    item('Identity', 'NeedsCheck', 'SourceData', 'Possible parcel mismatch — confirm the boundary', 'Confirm the parcel boundary'),
    item('Highway', 'Yes', 'SourceData', 'Yes — 0.6 mi to I 95 exit 13', 'Confirm the truck route'),
    item('Land', 'Yes', 'Estimate', 'Likely — ~6.9 open ac', 'Measure the usable area'),
    item('Capacity', 'Yes', 'Verified', 'Yes — 48 spaces (verified)'),
    item('TruckSpaces', 'Yes', 'Verified', 'Yes — 48 truck-suitable spaces (verified)'),
    item('Access', 'NeedsCheck', 'Verified', 'Entrance verified — turning space not checked', 'Check turning space'),
    item('Power', 'Unknown', 'Missing', 'Unknown — no capacity information', 'Ask the utility'),
    item('Contact', 'Unknown', 'Missing', 'No contact on file', 'Research the owner'),
  ],
  remainingToVerify: ['Confirm the parcel boundary', 'Confirm the truck route', 'Measure the usable area', 'Check turning space', 'Ask the utility', 'Research the owner'],
  yes: 4, needsCheck: 2, no: 0, unknown: 2, verifiedAnswers: 3,
  preferences: [
    item('PrefParking', 'Yes', 'Estimate', 'Yes — ~103 spaces (preliminary estimate)'),
    item('PrefHighway', 'Yes', 'SourceData', 'Yes — 0.62 mi to I 95 exit 13'),
    item('PrefIngress', 'Unknown', 'Missing', 'Unknown — not reviewed'),
    item('PrefBattery', 'Yes', 'Estimate', 'Possible — ~4.9 open ac beyond the parking (estimate)'),
  ],
  preferencesMet: 3,
};

describe('SiteSummaryComponent', () => {
  function render(s: SiteSuitability | null = summary) {
    const f = TestBed.createComponent(SiteSummaryComponent);
    f.componentRef.setInput('summary', s);
    f.detectChanges();
    return f;
  }

  it('renders one row per question with an answer icon and an honest basis label', () => {
    const el: HTMLElement = render().nativeElement;
    const rows = [...el.querySelectorAll('.suit-list > li')] as HTMLElement[];
    expect(rows.map(r => r.dataset['key'])).toEqual(['Identity', 'Highway', 'Land', 'Capacity', 'TruckSpaces', 'Access', 'Power', 'Contact']);
    const basis = (k: string) => el.querySelector(`li[data-key="${k}"] .suit-basis`)!.textContent!.trim();
    expect(basis('Land')).toBe('Estimate');
    expect(basis('Identity')).toBe('Public records');
    expect(basis('Capacity')).toBe('Verified');
    expect(basis('Power')).toBe('No data');
    expect(el.querySelector('li[data-key="Power"] .suit-icon')!.textContent!.trim()).toBe('?');
    expect(el.querySelector('li[data-key="Identity"] .suit-icon')!.textContent!.trim()).toBe('!');
    expect(el.querySelector('.suit-counts')!.textContent!.replace(/\s+/g, ' ')).toContain('3 of 8 answers verified');
  });

  it('lists what remains to be verified as question 9', () => {
    const el: HTMLElement = render().nativeElement;
    const box = el.querySelector('.suit-remaining')!;
    expect(box.textContent).toContain('9. What remains to be verified');
    expect(box.querySelectorAll('li').length).toBe(6);
  });

  it('shows details and the next step when a row is opened', () => {
    const f = render();
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelector('.suit-detail')).toBeNull();
    (el.querySelector('li[data-key="Power"] .suit-row') as HTMLButtonElement).click();
    f.detectChanges();
    const detail = el.querySelector('li[data-key="Power"] .suit-detail')!;
    expect(detail.textContent).toContain('Power detail');
    expect(detail.textContent).toContain('Next: Ask the utility');
    expect(el.querySelector('li[data-key="Power"] .suit-row')!.getAttribute('aria-expanded')).toBe('true');
  });

  it('never claims a winner or a score and renders nothing without data', () => {
    const el: HTMLElement = render().nativeElement;
    expect(el.textContent!.toLowerCase()).not.toMatch(/winner|recommended|best site/);
    expect(render(null).nativeElement.querySelector('.suit')).toBeNull();
  });

  it('shows preliminary business preferences separately from the C3 score', () => {
    const el: HTMLElement = render().nativeElement;
    const box = el.querySelector('.suit-prefs')!;
    expect(box.textContent).toContain('3 of 4 met');
    expect(box.textContent).toContain('not part of the C3 score');
    expect([...box.querySelectorAll('li')].map(l => (l as HTMLElement).dataset['key'])).toEqual(['PrefParking', 'PrefHighway', 'PrefIngress', 'PrefBattery']);
    expect(box.querySelector('li[data-key="PrefBattery"] .suit-basis')!.textContent!.trim()).toBe('Estimate');
    expect(box.querySelector('li[data-key="PrefIngress"] .suit-icon')!.textContent!.trim()).toBe('?');
  });
});
