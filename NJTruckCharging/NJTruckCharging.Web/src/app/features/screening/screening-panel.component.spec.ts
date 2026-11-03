import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SiteScreening } from '../../core/models';
import { ScreeningPanelComponent } from './screening-panel.component';

const notReviewed: SiteScreening = { siteId: 'S-1', decision: 'NotReviewed', evidence: null, updatedAtUtc: null, history: [] };

describe('ScreeningPanelComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function render(initial: SiteScreening = notReviewed) {
    const f = TestBed.createComponent(ScreeningPanelComponent);
    f.componentRef.setInput('siteId', initial.siteId);
    f.detectChanges();
    TestBed.tick();
    http.expectOne(r => r.url === '/api/screening' && r.params.get('siteId') === initial.siteId).flush(initial);
    f.detectChanges();
    return f;
  }
  const el = (f: { nativeElement: HTMLElement }) => f.nativeElement;
  const opt = (f: { nativeElement: HTMLElement }, d: string) => el(f).querySelector(`.decision-opt[data-d="${d}"]`) as HTMLButtonElement;
  const saveBtn = (f: { nativeElement: HTMLElement }) => [...el(f).querySelectorAll('button')].find(b => /Save decision/.test(b.textContent!)) as HTMLButtonElement;
  const typeEvidence = (f: { nativeElement: HTMLElement; detectChanges(): void }, text: string) => {
    const ta = el(f).querySelector('textarea')!;
    ta.value = text;
    ta.dispatchEvent(new Event('input'));
    f.detectChanges();
  };

  it('starts as Not reviewed with the save button disabled', () => {
    const f = render();
    expect(opt(f, 'NotReviewed').getAttribute('aria-checked')).toBe('true');
    expect(el(f).querySelector('.scr-head .decision-badge')!.textContent).toContain('Not reviewed');
    expect(saveBtn(f).disabled).toBe(true);
    expect(el(f).textContent).toContain('No decision recorded');
  });

  it('requires evidence before a decision can be saved', () => {
    const f = render();
    opt(f, 'Promising').click();
    f.detectChanges();
    expect(saveBtn(f).disabled).toBe(true);
    expect(el(f).querySelector('[role="alert"]')!.textContent).toContain('Add the evidence');

    typeEvidence(f, '   ');
    expect(saveBtn(f).disabled).toBe(true);
    typeEvidence(f, 'Satellite: paved 3 ac yard');
    expect(saveBtn(f).disabled).toBe(false);
  });

  it('saves the decision with trimmed evidence, emits it and shows history', () => {
    const f = render();
    const emitted: SiteScreening[] = [];
    f.componentInstance.saved.subscribe(s => emitted.push(s));

    opt(f, 'NeedsMoreResearch').click();
    typeEvidence(f, '  Gate on Street View — confirm access  ');
    saveBtn(f).click();

    const req = http.expectOne(r => r.method === 'PUT' && r.url === '/api/screening');
    expect(req.request.body).toEqual({ siteId: 'S-1', decision: 'NeedsMoreResearch', evidence: 'Gate on Street View — confirm access' });
    const savedState: SiteScreening = {
      siteId: 'S-1', decision: 'NeedsMoreResearch', evidence: 'Gate on Street View — confirm access', updatedAtUtc: '2026-11-02T10:00:00Z',
      history: [{ decision: 'NeedsMoreResearch', evidence: 'Gate on Street View — confirm access', recordedAtUtc: '2026-11-02T10:00:00Z' }],
    };
    req.flush(savedState);
    f.detectChanges();

    expect(emitted.map(e => e.decision)).toEqual(['NeedsMoreResearch']);
    expect(el(f).querySelector('.scr-head .decision-badge')!.textContent).toContain('Needs more research');
    expect(el(f).querySelector('.scr-history')!.textContent).toContain('Assessment history (1)');
    expect(saveBtn(f).disabled).toBe(true); // nothing left to save
  });

  it('shows the API validation message when a save is rejected', () => {
    const f = render();
    opt(f, 'Unsuitable').click();
    typeEvidence(f, 'x');
    saveBtn(f).click();
    http.expectOne(r => r.method === 'PUT').flush('Evidence is required: describe what you observed', { status: 400, statusText: 'Bad Request' });
    f.detectChanges();
    expect(el(f).querySelector('.banner.error')!.textContent).toContain('Evidence is required');
  });

  it('loads an existing decision and lets the user reset to Not reviewed without evidence', () => {
    const f = render({ siteId: 'S-2', decision: 'Promising', evidence: 'Yard visible', updatedAtUtc: '2026-11-01T09:00:00Z',
      history: [{ decision: 'Promising', evidence: 'Yard visible', recordedAtUtc: '2026-11-01T09:00:00Z' }] });
    expect(opt(f, 'Promising').getAttribute('aria-checked')).toBe('true');
    expect(el(f).querySelector('textarea')!.value).toBe('Yard visible');

    opt(f, 'NotReviewed').click();
    typeEvidence(f, '');
    f.detectChanges();
    expect(saveBtn(f).disabled).toBe(false);
    saveBtn(f).click();
    const req = http.expectOne(r => r.method === 'PUT');
    expect(req.request.body.decision).toBe('NotReviewed');
    expect(req.request.body.evidence).toBeNull();
    req.flush({ ...notReviewed, siteId: 'S-2' });
  });
});
