import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { MethodologyStore } from '../../core/methodology-store';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CompareStore } from '../../core/compare-store';
import { ComparedSite, ComparisonRow, SiteComparison } from '../../core/models';
import { ComparePage, groupRows } from './compare.page';

const unknown = { status: 'Unknown' as const, evidence: null, reviewedAtUtc: null };

function site(siteId: string, segment: number, over: Partial<ComparedSite> = {}): ComparedSite {
  return {
    siteId, candidateId: 1, address: `${siteId} Address`, municipality: 'Edison', county: 'Middlesex',
    segment, segmentCounties: 'X', score: 70, segmentRank: 3, sitesInSegment: 5000,
    propClass: '4B', propertyClassLabel: 'Industrial', totalAcres: 10, openAcresEst: null,
    ios: { classification: 'FreightIndicated', basis: 'SourceDerived', evidence: 'e', derivedClassification: 'FreightIndicated', reviewStatus: 'Unknown' },
    nearestHighway: 'I-95', nearestExit: '10', exitName: null, milesToExit: 0.5,
    highwayAccess: unknown, truckEntrance: unknown, estimatedParkingPreliminary: null, verifiedParking: unknown,
    utility: 'PSE&G', electrical: unknown,
    flood: { status: 'NotChecked', detail: 'Not screened' }, contamination: { status: 'NotChecked', detail: 'Not screened' },
    highlands: { status: 'Clear', detail: 'None' }, floodEnvironmentalReview: unknown,
    missingVerification: ['Truck entrance & access', 'Electrical service / capacity'], concerns: [],
    screeningDecision: 'NotReviewed', screeningEvidence: null,
    lat: 40.5, lng: -74.3, googleMapsUrl: `https://www.google.com/maps/search/?api=1&query=${siteId}`,
    satelliteUrl: `https://www.google.com/maps/@?api=1&map_action=map&center=${siteId}&basemap=satellite`,
    streetViewUrl: `https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=${siteId}`,
    ...over,
  };
}

const row = (key: string, group: string, values: string[], tones: string[], differs: boolean, comparable = true): ComparisonRow =>
  ({ key, group, label: key, hint: null, values, tones: tones as ComparisonRow['tones'], comparable, differs });

const fixture: SiteComparison = {
  sites: [site('A-1', 1), site('B-2', 4, { truckEntrance: { status: 'Verified', evidence: 'curb cuts', reviewedAtUtc: null } })],
  rows: [
    row('address', 'Location', ['a', 'b'], ['', ''], false, false),
    row('segment', 'Location', ['Segment 1', 'Segment 4'], ['', ''], true),
    row('score', 'C3 score', ['70.0', '70.0'], ['', ''], false),
    row('truckEntrance', 'Highway access', ['Not reviewed', 'Verified — curb cuts'], ['unknown', 'verified'], true),
    row('verifiedParking', 'Parking', ['Not verified', 'Not verified'], ['unknown', 'unknown'], false),
    row('missing', 'Verification', ['2: …', '1: …'], ['unknown', 'unknown'], true),
    row('maps', 'Links', ['u1', 'u2'], ['', ''], false, false),
  ],
  missingSiteIds: [],
  mixedSegments: true,
  scoreNote: 'C3 scores and ranks are relative to each site\'s own NOFA segment.',
};

describe('groupRows', () => {
  it('keeps API order, drops header-only rows and can show only differences', () => {
    const all = groupRows(fixture.rows, false);
    expect(all.map(g => g.group)).toEqual(['Location', 'C3 score', 'Highway access', 'Parking', 'Verification']);
    expect(all.flatMap(g => g.rows).some(r => r.key === 'address' || r.key === 'maps')).toBe(false);

    const diff = groupRows(fixture.rows, true).flatMap(g => g.rows.map(r => r.key));
    expect(diff).toEqual(['segment', 'truckEntrance', 'missing']);
  });
});

describe('ComparePage', () => {
  let http: HttpTestingController;
  let store: CompareStore;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: MethodologyStore, useValue: { epoch: signal(0) } }],
    });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(CompareStore);
  });

  afterEach(() => http.verify());

  async function render() {
    const f = TestBed.createComponent(ComparePage);
    f.detectChanges();
    TestBed.tick(); // run the resource's request (an HTTP request in flight keeps the app "unstable")
    const req = http.expectOne(r => r.url === '/api/compare');
    expect(req.request.params.getAll('siteIds')).toEqual(['A-1', 'B-2']);
    req.flush(fixture);
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  it('asks the user to pick at least two sites and makes no request', async () => {
    store.add({ siteId: 'A-1', label: 'A', segment: 1 });
    const f = TestBed.createComponent(ComparePage);
    f.detectChanges();
    await f.whenStable();
    http.expectNone('/api/compare');
    expect(f.nativeElement.textContent).toContain('Select at least 2 sites');
  });

  it('renders sites side by side with unknown values never shown as verified', async () => {
    store.add({ siteId: 'A-1', label: 'A', segment: 1 });
    store.add({ siteId: 'B-2', label: 'B', segment: 4 });
    const f = await render();
    const el: HTMLElement = f.nativeElement;

    const headers = [...el.querySelectorAll('th.site-col .cs-addr')].map(h => h.textContent?.trim());
    expect(headers).toEqual(['A-1 Address', 'B-2 Address']);

    const entrance = [...el.querySelectorAll('tr[data-key="truckEntrance"] td')] as HTMLElement[];
    expect(entrance[0].dataset['tone']).toBe('unknown');
    expect(entrance[0].textContent).toContain('Not reviewed');
    expect(entrance[1].dataset['tone']).toBe('verified');

    const parking = [...el.querySelectorAll('tr[data-key="verifiedParking"] td')] as HTMLElement[];
    parking.forEach(td => { expect(td.dataset['tone']).toBe('unknown'); expect(td.textContent).not.toContain('Verified'); });

    // Missing items render as a list from the structured data.
    expect(el.querySelector('tr[data-key="missing"] td')!.textContent).toContain('2 outstanding');
  });

  it('labels segment-relative scores, highlights differences and never names a winner', async () => {
    store.add({ siteId: 'A-1', label: 'A', segment: 1 });
    store.add({ siteId: 'B-2', label: 'B', segment: 4 });
    const f = await render();
    const el: HTMLElement = f.nativeElement;

    expect(el.querySelector('.score-note.mixed')!.textContent).toContain('Different segments selected');
    expect(el.querySelector('tr[data-key="segment"]')!.classList).toContain('differs');
    expect(el.querySelector('tr[data-key="score"]')!.classList).not.toContain('differs');
    const text = el.textContent!.toLowerCase();
    expect(text).not.toMatch(/winner|recommended|best site/);

    const maps = [...el.querySelectorAll('th.site-col a')].map(a => (a as HTMLAnchorElement).href);
    expect(maps[0]).toContain('google.com/maps');

    const exportLink = [...el.querySelectorAll('a')].find(a => a.textContent?.includes('Export CSV')) as HTMLAnchorElement;
    expect(exportLink.getAttribute('href')).toBe('/api/compare/export?siteIds=A-1&siteIds=B-2');
  });

  it('"Only rows that differ" hides identical rows', async () => {
    store.add({ siteId: 'A-1', label: 'A', segment: 1 });
    store.add({ siteId: 'B-2', label: 'B', segment: 4 });
    const f = await render();
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelector('tr[data-key="score"]')).not.toBeNull();

    const box = el.querySelector('.toggle-inline input') as HTMLInputElement;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    f.detectChanges();
    expect(el.querySelector('tr[data-key="score"]')).toBeNull();
    expect(el.querySelector('tr[data-key="truckEntrance"]')).not.toBeNull();
  });

  it('removing a site updates the shared selection', async () => {
    store.add({ siteId: 'A-1', label: 'A', segment: 1 });
    store.add({ siteId: 'B-2', label: 'B', segment: 4 });
    const f = await render();
    const remove = f.nativeElement.querySelector('th.site-col .danger') as HTMLButtonElement;
    remove.click();
    f.detectChanges();
    expect(store.siteIds()).toEqual(['B-2']);
    expect(f.nativeElement.textContent).toContain('Select at least 2 sites');
  });
});
