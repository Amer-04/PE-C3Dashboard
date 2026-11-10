import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ResearchSummary, ReviewQueue } from '../../core/models';
import { ResearchReviewComponent } from './research-review.component';

const queue: ReviewQueue = {
  researchSites: 3, sitesWithIssues: 2, excludedUnsuitable: 1,
  issueCounts: { ParcelMismatch: 1, BusinessConflict: 0, ConflictingEvidence: 0, UnverifiedContact: 0, TruckAccessUnchecked: 1, ElectricalUnknown: 2, InsufficientEvidence: 0 },
  items: [
    { id: 1, siteId: 'A', segment: 1, rank: 1, address: '1 Oak Ave', municipality: 'Lawnside', county: 'Camden', reviewed: 2, verified: 1, total: 8,
      issues: [{ issue: 'ParcelMismatch', label: 'Parcel mismatch' }, { issue: 'ElectricalUnknown', label: 'Electrical feasibility unknown' }] },
    { id: 2, siteId: 'B', segment: 3, rank: 4, address: '2 Bay Way', municipality: 'Elizabeth', county: 'Union', reviewed: 0, verified: 0, total: 8,
      issues: [{ issue: 'TruckAccessUnchecked', label: 'Truck access not checked' }, { issue: 'ElectricalUnknown', label: 'Electrical feasibility unknown' }] },
  ],
};
const summary: ResearchSummary = {
  researchSites: 3, noneReviewed: 1, partlyReviewed: 1, allReviewed: 1, checksReviewed: 10, checksVerified: 8, checksTotal: 24, sitesWithIssues: 2,
  segments: [{ segment: 1, researchSites: 2, noneReviewed: 0, partlyReviewed: 1, allReviewed: 1, checksReviewed: 10, checksVerified: 8, checksTotal: 16, sitesWithIssues: 1 },
             { segment: 3, researchSites: 1, noneReviewed: 1, partlyReviewed: 0, allReviewed: 0, checksReviewed: 0, checksVerified: 0, checksTotal: 8, sitesWithIssues: 1 }],
};

describe('ResearchReviewComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function render() {
    const f = TestBed.createComponent(ResearchReviewComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/research/queue').flush(queue);
    http.expectOne(r => r.url === '/api/research/summary').flush(summary);
    f.detectChanges();
    return f;
  }
  const el = (f: { nativeElement: HTMLElement }) => f.nativeElement;

  it('lists sites with their completeness and open issues, and notes the resolved Unsuitable sites', () => {
    const f = render();
    const rows = el(f).querySelectorAll('.rr-table tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('1 Oak Ave');
    expect(rows[0].textContent).toContain('2/8 reviewed · 1 verified');
    expect(rows[0].querySelectorAll('.rr-badge').length).toBe(2);
    expect(el(f).textContent).toContain('1 screened Unsuitable (resolved)');
  });

  it('shows research completeness for all six segments, separately from the score', () => {
    const f = render();
    const segs = el(f).querySelectorAll('.rr-seg');
    expect(segs.length).toBe(6);
    expect(segs[0].textContent).toContain('10/16 checks');
    expect(segs[1].textContent).toContain('0 sites');
    expect(el(f).textContent).toContain('Separate from the C3 score');
    expect(el(f).textContent).not.toMatch(/\bScore\b/);
  });

  it('filters by segment and by issue through the API', () => {
    const f = render();
    (el(f).querySelectorAll('.rr-seg')[2] as HTMLButtonElement).click();
    f.detectChanges();
    const bySeg = http.expectOne(r => r.url === '/api/research/queue');
    expect(bySeg.request.params.getAll('segments')).toEqual(['3']);
    bySeg.flush(queue);

    const chip = [...el(f).querySelectorAll('.rr-issues .chip')].find(c => c.textContent!.includes('Parcel mismatch')) as HTMLButtonElement;
    chip.click();
    f.detectChanges();
    const byIssue = http.expectOne(r => r.url === '/api/research/queue');
    expect(byIssue.request.params.getAll('issues')).toEqual(['ParcelMismatch']);
    expect(byIssue.request.params.getAll('segments')).toEqual(['3']);
    byIssue.flush(queue);
  });

  it('keeps a just-resolved site on screen, marked resolved, until it is closed', () => {
    const f = render();
    const resolve = [...el(f).querySelectorAll('button')].find(b => b.textContent!.trim() === 'Resolve') as HTMLButtonElement;
    resolve.click();
    f.detectChanges();
    TestBed.tick();
    http.match(r => r.params.get('siteId') === 'A').forEach(r => r.flush(r.request.url.endsWith('/research/site') ? null : r.request.url.endsWith('summary') ? {} : []));

    // Evidence recorded in the panel → the queue no longer lists site A.
    (f.componentInstance as unknown as { refresh(): void }).refresh();
    http.expectOne(r => r.url === '/api/research/queue').flush({ ...queue, items: [queue.items[1]] });
    http.expectOne(r => r.url === '/api/research/summary').flush(summary);
    http.expectOne(r => r.url === '/api/research/site' && r.params.get('siteId') === 'A').flush({ reviewed: 3, verified: 2 });
    f.detectChanges();
    const first = el(f).querySelector('.rr-table tbody tr')!;
    expect(first.textContent).toContain('1 Oak Ave');
    expect(first.textContent).toContain('Resolved — no open issues');
    expect(first.textContent).toContain('3/8 reviewed · 2 verified');
    http.match(() => true).forEach(r => r.flush([]));

    // Closing it removes it.
    ([...el(f).querySelectorAll('button')].find(b => b.textContent!.trim() === 'Close') as HTMLButtonElement).click();
    f.detectChanges();
    expect(el(f).textContent).not.toContain('1 Oak Ave');
  });

  it('opens the existing feasibility and contact panels to resolve a site', () => {
    const f = render();
    const resolve = [...el(f).querySelectorAll('button')].find(b => b.textContent!.trim() === 'Resolve') as HTMLButtonElement;
    resolve.click();
    f.detectChanges();
    TestBed.tick();
    expect(el(f).querySelector('app-feasibility-panel')).not.toBeNull();
    expect(el(f).querySelector('app-contacts-panel')).not.toBeNull();
    // The panels load the site's research records.
    http.match(r => r.params.get('siteId') === 'A').forEach(r => r.flush(r.request.url.endsWith('/research/site') ? null : r.request.url.endsWith('summary') ? {} : []));
  });
});
