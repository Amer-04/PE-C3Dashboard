import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FeasibilityItem, ResearchHistoryEntry } from '../../core/models';
import { RecordedByStore } from '../../core/recorded-by';
import { FeasibilityRowComponent, todayIso, verdictProblem } from './feasibility-row.component';

const base = { evidence: 'Tax map matches', sourceUrl: 'https://example.org/map', evidenceDate: '2026-10-01', isCorrection: false, note: '', recordedBy: 'Dana' };

describe('verdictProblem', () => {
  it('requires evidence, a source URL and a date for Verified and Concern', () => {
    expect(verdictProblem({ ...base, status: 'Verified' })).toBeNull();
    expect(verdictProblem({ ...base, status: 'Concern' })).toBeNull();
    expect(verdictProblem({ ...base, status: 'Verified', evidence: '  ' })).toContain('evidence');
    expect(verdictProblem({ ...base, status: 'Verified', sourceUrl: '' })).toContain('source URL');
    expect(verdictProblem({ ...base, status: 'Concern', sourceUrl: 'javascript:alert(1)' })).toContain('source URL');
    expect(verdictProblem({ ...base, status: 'Verified', evidenceDate: '' })).toContain('date');
  });

  it('requires Conflicting to say what disagrees, and nothing for Unknown', () => {
    expect(verdictProblem({ ...base, status: 'Conflicting', evidence: '', sourceUrl: '', evidenceDate: '' })).toContain('disagree');
    expect(verdictProblem({ ...base, status: 'Unknown', evidence: '', sourceUrl: '', evidenceDate: '' })).toBeNull();
  });

  it('rejects future evidence dates and corrections without a reason', () => {
    expect(verdictProblem({ ...base, status: 'Verified', evidenceDate: '2999-01-01' })).toContain('future');
    expect(verdictProblem({ ...base, status: 'Verified', isCorrection: true })).toContain('reason');
    expect(verdictProblem({ ...base, status: 'Verified', isCorrection: true, note: 'Second letter' })).toBeNull();
  });

  it('requires a Recorded-by name for Verified and Concern, but not for Conflicting or Unknown', () => {
    expect(verdictProblem({ ...base, status: 'Verified', recordedBy: '' })).toContain('Recorded by');
    expect(verdictProblem({ ...base, status: 'Verified', recordedBy: '   ' })).toContain('not authenticated');
    expect(verdictProblem({ ...base, status: 'Concern', recordedBy: '' })).toContain('Recorded by');
    expect(verdictProblem({ ...base, status: 'Conflicting', recordedBy: '' })).toBeNull();
    expect(verdictProblem({ ...base, status: 'Unknown', recordedBy: '' })).toBeNull();
  });

  it('todayIso is yyyy-MM-dd', () => expect(todayIso()).toMatch(/^\d{4}-\d{2}-\d{2}$/));
});

describe('FeasibilityRowComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    try { localStorage.removeItem('c3.recordedBy'); } catch { /* ignore */ }
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function render(item: Partial<FeasibilityItem> = {}) {
    const f = TestBed.createComponent(FeasibilityRowComponent);
    f.componentRef.setInput('siteId', 'S-1');
    f.componentRef.setInput('label', 'Zoning');
    f.componentRef.setInput('core', true);
    f.componentRef.setInput('item', {
      itemKey: 'Zoning', status: 'Unknown', evidence: null, reviewer: null, reviewedAtUtc: null, updatedAtUtc: null, ...item,
    } as FeasibilityItem);
    f.detectChanges();
    return f;
  }
  const el = (f: { nativeElement: HTMLElement }) => f.nativeElement;
  const btn = (f: { nativeElement: HTMLElement }, text: string) => [...el(f).querySelectorAll('button')].find(b => b.textContent!.trim() === text) as HTMLButtonElement;
  const set = (f: { nativeElement: HTMLElement; detectChanges(): void }, sel: string, value: string, ev = 'input') => {
    const e = el(f).querySelector(sel) as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
    e.value = value;
    e.dispatchEvent(new Event(ev));
    f.detectChanges();
  };

  it('flags an existing undocumented verdict as insufficient evidence instead of hiding or upgrading it', () => {
    const f = render({ status: 'Verified', evidence: 'curb cuts', evidenceState: 'InsufficientEvidence', reviewedAtUtc: '2026-10-01T00:00:00Z' });
    expect(el(f).querySelector('.ev-flag')!.textContent).toContain('Insufficient evidence');
    expect(el(f).querySelector('.ev-flag')!.textContent).toContain('source URL, evidence date or Recorded-by name');
    expect(el(f).querySelector('.core-tag')).not.toBeNull();
  });

  it('flags a legacy verdict that has a source and date but no Recorded-by name', () => {
    const f = render({ status: 'Verified', evidence: 'Tax map', sourceUrl: 'https://example.org/a', evidenceDate: '2026-09-01',
      reviewer: null, evidenceState: 'InsufficientEvidence', reviewedAtUtc: '2026-10-01T00:00:00Z' });
    const flag = el(f).querySelector('.ev-flag')!.textContent!;
    expect(flag).toContain('Insufficient evidence');
    expect(flag).toContain('no Recorded-by name');
  });

  it('keeps Save disabled until a Recorded-by name is entered, and labels it self-reported', () => {
    const f = render({ reviewer: 'Dana', reviewedAtUtc: '2026-10-01T00:00:00Z', status: 'Concern', evidence: 'x', evidenceState: 'Concern' });
    expect(el(f).querySelector('.fr-foot')!.textContent).toContain('(self-reported, not authenticated)');
    set(f, 'select', 'Verified', 'change');
    set(f, 'textarea', 'Zoning map: I-2');
    set(f, 'input[type="url"]', 'https://example.org/zoning');
    set(f, 'input[type="date"]', '2026-10-01');
    set(f, '.fr-note', 'Rechecked');
    expect(btn(f, 'Save').disabled).toBe(true);
    expect(el(f).textContent).toContain('Recorded by');
    TestBed.inject(RecordedByStore).set('Dana');
    f.detectChanges();
    expect(btn(f, 'Save').disabled).toBe(false);
  });

  it('only enables Save once evidence, source and date are present, and sends them with the self-reported name', () => {
    TestBed.inject(RecordedByStore).set('Dana');
    const f = render();
    set(f, 'select', 'Verified', 'change');
    expect(btn(f, 'Save').disabled).toBe(true);
    set(f, 'textarea', 'Zoning map: I-2');
    set(f, 'input[type="url"]', 'https://example.org/zoning');
    expect(btn(f, 'Save').disabled).toBe(true);
    set(f, 'input[type="date"]', '2026-10-01');
    expect(btn(f, 'Save').disabled).toBe(false);

    btn(f, 'Save').click();
    const req = http.expectOne('/api/feasibility');
    expect(req.request.body).toEqual(expect.objectContaining({
      siteId: 'S-1', itemKey: 'Zoning', status: 'Verified', evidence: 'Zoning map: I-2',
      sourceUrl: 'https://example.org/zoning', evidenceDate: '2026-10-01', recordedBy: 'Dana',
    }));
    req.flush({});
  });

  it('asks for a correction reason when an earlier verdict is changed', () => {
    TestBed.inject(RecordedByStore).set('Dana');
    const f = render({ status: 'Verified', evidence: 'ok', sourceUrl: 'https://example.org/a', evidenceDate: '2026-09-01', evidenceState: 'Verified', reviewedAtUtc: '2026-09-02T00:00:00Z' });
    expect(el(f).querySelector('.fr-note')).toBeNull();
    set(f, 'select', 'Concern', 'change');
    expect(el(f).querySelector('.fr-note')).not.toBeNull();
    expect(btn(f, 'Save').disabled).toBe(true);
    set(f, '.fr-note', 'Revised utility letter');
    expect(btn(f, 'Save').disabled).toBe(false);
  });

  it('shows this check\'s history, with earlier verdicts and reasons', () => {
    const f = render({ status: 'Concern', evidence: 'x', evidenceState: 'Concern' });
    btn(f, 'History').click();
    f.detectChanges();
    const entry = (over: Partial<ResearchHistoryEntry>): ResearchHistoryEntry => ({
      id: 1, siteId: 'S-1', subject: 'Feasibility', action: 'Recorded', itemKey: 'Zoning', contactId: null, previousStatus: 'Unknown',
      status: 'Verified', evidence: 'first', sourceUrl: 'https://example.org/a', evidenceDate: '2026-09-01', note: null,
      recordedBy: 'Dana', recordedAtUtc: '2026-09-02T10:00:00Z', ...over,
    });
    http.expectOne(r => r.url === '/api/research/history').flush([
      entry({ id: 2, action: 'Corrected', previousStatus: 'Verified', status: 'Concern', evidence: 'second', note: 'New letter' }),
      entry({ id: 1 }),
      entry({ id: 3, itemKey: 'ElectricalService', evidence: 'other check' }),
    ]);
    f.detectChanges();
    const items = el(f).querySelectorAll('.fr-history li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Corrected');
    expect(items[0].textContent).toContain('Reason: New letter');
    expect(items[1].textContent).toContain('first');
    expect(el(f).querySelector('.fr-history')!.textContent).toContain('(self-reported, not authenticated)');
  });
});
