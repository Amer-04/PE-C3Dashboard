import { TestBed } from '@angular/core/testing';
import { DEFAULT_FILTER, SiteSuitability, SiteValidation, ValidationSection } from '../../core/models';
import { toParams } from '../../core/api.service';
import { SiteSummaryComponent } from './site-summary.component';

const sec = (key: ValidationSection['key'], status: ValidationSection['status'], extra: Partial<ValidationSection> = {}): ValidationSection => ({
  key, title: `${key} title`, status, statusLabel: status === 'Unconfirmed' ? 'Not verified' : status,
  facts: [{ label: 'Block / lot', value: 'Block 749, Lot 1' }], finding: null, evidenceLinks: [], checkedAtUtc: null,
  discrepancies: [], nextStep: null, ...extra,
});

const emptySummary: SiteSuitability = { items: [], remainingToVerify: [], yes: 0, needsCheck: 0, no: 0, unknown: 0, verifiedAnswers: 0, preferences: [], preferencesMet: 0 };

describe('Site validation sections (At a glance)', () => {
  function render(v: SiteValidation) {
    const f = TestBed.createComponent(SiteSummaryComponent);
    f.componentRef.setInput('summary', emptySummary);
    f.componentRef.setInput('validation', v);
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }

  it('keeps parcel, business, owner and suitability apart with honest Unknown states', () => {
    const el = render({
      siteEvidence: 'Unknown', contactReadiness: 'NoContact', missingVerification: ['Confirm block/lot'],
      sections: [sec('Parcel', 'Unconfirmed'), sec('Business', 'Unknown'), sec('Owner', 'Unknown'), sec('Suitability', 'Unconfirmed')],
    });
    const keys = [...el.querySelectorAll('.val-sec')].map(s => (s as HTMLElement).dataset['key']);
    expect(keys).toEqual(['Parcel', 'Business', 'Owner', 'Suitability']);
    expect(el.querySelector('.val-head')!.textContent).toContain('Unknown');
    expect(el.querySelector('.val-head')!.textContent).toContain('No Contact');
    expect(el.querySelector('.val-sec[data-key="Parcel"] .val-meta')!.textContent).toContain('Checked: never');
    expect(el.textContent).not.toContain('Verified');
  });

  it('shows evidence links, checked dates and discrepancies', () => {
    const el = render({
      siteEvidence: 'Mismatch', contactReadiness: 'GeneralBusinessContact', missingVerification: [],
      sections: [
        sec('Parcel', 'Verified', { evidenceLinks: ['https://tax.example/map'], checkedAtUtc: '2026-11-02T10:00:00Z' }),
        sec('Business', 'Mismatch', { discrepancies: ['Reviewer: listed business moved out'] }),
        sec('Owner', 'Unconfirmed'), sec('Suitability', 'Unconfirmed'),
      ],
    });
    const parcel = el.querySelector('.val-sec[data-key="Parcel"]')!;
    expect(parcel.querySelector('a')!.getAttribute('href')).toBe('https://tax.example/map');
    expect(parcel.querySelector('.val-meta')!.textContent).toContain('Nov 2, 2026');
    expect(el.querySelector('.val-sec[data-key="Business"] .val-disc')!.textContent).toContain('moved out');
    expect(el.querySelector('.val-head')!.textContent).toContain('General Business Contact');
  });
});

describe('Research readiness filters', () => {
  it('serialise as repeated query keys', () => {
    const p = toParams({ ...DEFAULT_FILTER, evidenceStates: ['verified', 'mismatch'], contactStates: ['relevant'] });
    expect(p.getAll('evidenceStates')).toEqual(['verified', 'mismatch']);
    expect(p.getAll('contactStates')).toEqual(['relevant']);
    expect(toParams(DEFAULT_FILTER).has('evidenceStates')).toBe(false);
  });
});
