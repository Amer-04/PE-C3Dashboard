import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { PagedResult, SiteDetail, SiteListItem } from '../../core/models';
import { SatelliteMapComponent } from './satellite-map.component';
import { ScreeningPage, contaminationText, floodText, verdictText } from './screening.page';

@Component({ selector: 'app-satellite-map', template: '<div class="sat-stub"></div>' })
class SatelliteMapStub { readonly lat = input<number>(); readonly lng = input<number>(); readonly label = input<string>(); }

function item(n: number, over: Partial<SiteListItem> = {}): SiteListItem {
  return {
    id: n, siteId: `S-${n}`, segment: 3, rank: n, score: 100 - n, fileScore: 0, address: `${n} Bayway`, municipality: 'Elizabeth',
    county: 'Union', propClass: '4B', acres: 10, openAcres: 4, milesToExit: 0.6, nearestHighway: 'I 95', nearestExit: '13',
    trucksBased5Mi: 0, truckTraffic: 0, largeWarehouses2Mi: 0, businesses: null, freight: true, utility: 'PSE&G',
    inFloodZone: false, floodChecked: false, isContaminated: false, inHighlandsPreservation: false, nearPowerEdison: false,
    nearestPowerEdisonSite: null, milesToPowerEdison: 9, lat: 40.6, lng: -74.2, estimatedParking: 60,
    iosClassification: 'FreightIndicated', iosBasis: 'SourceDerived', screeningDecision: 'NotReviewed',
    siteEvidence: 'Unknown', contactReadiness: 'NoContact', ...over,
  };
}
function detail(s: SiteListItem): SiteDetail {
  return {
    summary: s, scoreLines: [], truckDemandPoints: 0, sitesInSegment: 5732, floodZone100Yr: null, knownContaminatedSites: null, highlands: null,
    truckEntranceStatus: 'Unknown', truckEntranceEvidence: null, electricalStatus: 'Unknown', electricalEvidence: null,
    parkingStatus: 'Unknown', verifiedParkingEvidence: null, screeningEvidence: null, screeningUpdatedAtUtc: null,
    googleMapsUrl: 'https://www.google.com/maps/search/?api=1&query=40.6,-74.2',
    satelliteUrl: 'https://www.google.com/maps/@?api=1&map_action=map&center=40.6,-74.2&zoom=19&basemap=satellite',
    streetViewUrl: 'https://www.google.com/maps/@?api=1&map_action=pano&viewpoint=40.6,-74.2',
    nearbyFleets: [],
  } as unknown as SiteDetail;
}
const seg = (segment: number, total: number, promising = 0, unsuitable = 0) =>
  ({ segment, total, promising, needsMoreResearch: 0, unsuitable, notReviewed: total - promising - unsuitable, reviewed: promising + unsuitable });
const summary = (promising = 1, unsuitable = 0) => {
  const segments = [seg(1, 25), seg(2, 25), seg(3, 25, promising, unsuitable), seg(4, 25), seg(5, 25), seg(6, 25)];
  const sum = (k: 'total' | 'promising' | 'needsMoreResearch' | 'unsuitable' | 'notReviewed' | 'reviewed') => segments.reduce((a, g) => a + g[k], 0);
  return { total: sum('total'), notReviewed: sum('notReviewed'), promising: sum('promising'), needsMoreResearch: 0, unsuitable: sum('unsuitable'),
    reviewed: sum('reviewed'), topN: 25, segments };
};
const isSummary = (r: { url: string }) => r.url === '/api/screening/summary';

const page = (items: SiteListItem[]): PagedResult<SiteListItem> => ({ items, page: 1, pageSize: 25, totalCount: items.length, totalPages: 1 });

describe('screening fact helpers', () => {
  it('never present unchecked or unreviewed values as clear/verified', () => {
    expect(floodText({ floodZone100Yr: null }).tone).toBe('unknown');
    expect(floodText({ floodZone100Yr: 'none' }).tone).toBe('clear');
    expect(floodText({ floodZone100Yr: 'AE' }).text).toContain('AE');
    expect(contaminationText({ knownContaminatedSites: null }).text).toContain('Not checked');
    expect(contaminationText({ knownContaminatedSites: 2 }).tone).toBe('flag');
    expect(verdictText('Unknown', 'stale note')).toEqual({ text: 'Not reviewed', tone: 'unknown' });
    expect(verdictText('Verified', '48 stalls').text).toBe('Verified — 48 stalls');
  });
});

describe('ScreeningPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.overrideComponent(ScreeningPage, { remove: { imports: [SatelliteMapComponent] }, add: { imports: [SatelliteMapStub] } });
    http = TestBed.inject(HttpTestingController);
  });

  function render(items = [item(1), item(2, { screeningDecision: 'Promising' })]) {
    const f = TestBed.createComponent(ScreeningPage);
    const queue = http.expectOne(r => r.url === '/api/sites');
    expect(queue.request.params.get('sortBy')).toBe('Score');
    expect(queue.request.params.get('topN')).toBe('25');
    queue.flush(page(items));
    const sumReq = http.expectOne(isSummary);
    expect(sumReq.request.params.get('topN')).toBe('25');
    sumReq.flush(summary());
    http.expectOne('/api/saved-sites/refs').flush([]);
    http.expectOne('/api/sites/1').flush(detail(items[0]));
    f.detectChanges();
    TestBed.tick();
    http.expectOne(r => r.url === '/api/screening' && r.params.get('siteId') === 'S-1')
      .flush({ siteId: 'S-1', decision: 'NotReviewed', evidence: null, updatedAtUtc: null, history: [] });
    f.detectChanges();
    return f;
  }

  it('shows a ranked queue, the satellite view and links, and unknown facts as unknown', () => {
    const f = render();
    const el: HTMLElement = f.nativeElement;
    expect([...el.querySelectorAll('.queue-list li .decision-badge')].map(b => b.textContent!.trim())).toEqual(['Not reviewed', 'Promising']);
    expect(el.querySelector('.sat-stub')).not.toBeNull();
    const links = [...el.querySelectorAll('.scr-imagery a')].map(a => a.getAttribute('href'));
    expect(links.some(h => h!.includes('basemap=satellite'))).toBe(true);
    expect(links.some(h => h!.includes('map_action=pano'))).toBe(true);

    const dts = [...el.querySelectorAll('.scr-facts dl dt')];
    const fact = (label: string) => {
      const dd = dts.find(d => d.textContent!.trim() === label)!.nextElementSibling as HTMLElement;
      return { text: dd.textContent!.replace(/\s+/g, ' ').trim(), tone: dd.dataset['tone'] };
    };
    expect(fact('Truck entrance')).toEqual({ text: 'Not reviewed', tone: 'unknown' });
    expect(fact('Verified parking')).toEqual({ text: 'Not reviewed', tone: 'unknown' });
    expect(fact('Parking (prelim.)').text).toContain('preliminary, not verified');
    expect(fact('Flood')).toEqual({ text: 'Not checked in source data', tone: 'unknown' });
    expect(fact('Contamination').tone).toBe('unknown');
    expect(fact('IOS').text).toContain('Source data — not verified');
    expect(el.querySelector('.scr-summary')!.textContent).toContain('149');           // not reviewed in scope
    expect(el.querySelector('.scr-scope')!.textContent!.replace(/\s+/g, ' ')).toContain('1 of 150 reviewed');
    http.verify();
  });

  it('filters the queue by decision from the progress strip', () => {
    const f = render();
    (f.nativeElement.querySelector('.scr-stat[data-d="Promising"]') as HTMLButtonElement).click();
    const req = http.expectOne(r => r.url === '/api/sites');
    expect(req.request.params.getAll('screeningDecisions')).toEqual(['promising']);
    req.flush(page([]));
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('No candidates match');
  });

  it('updates the queue badge after a decision is saved and steps to the next site', () => {
    const f = render();
    const el: HTMLElement = f.nativeElement;
    (el.querySelector('.decision-opt[data-d="Unsuitable"]') as HTMLButtonElement).click();
    const ta = el.querySelector('.scr-facts textarea') as HTMLTextAreaElement;
    ta.value = 'Wetlands across the rear of the parcel (2025 imagery)';
    ta.dispatchEvent(new Event('input'));
    f.detectChanges();
    ([...el.querySelectorAll('button')].find(b => /Save decision/.test(b.textContent!)) as HTMLButtonElement).click();
    http.expectOne(r => r.method === 'PUT' && r.url === '/api/screening').flush({
      siteId: 'S-1', decision: 'Unsuitable', evidence: ta.value, updatedAtUtc: '2026-11-02T10:00:00Z',
      history: [{ decision: 'Unsuitable', evidence: ta.value, recordedAtUtc: '2026-11-02T10:00:00Z' }],
    });
    http.expectOne(isSummary).flush(summary(1, 1));
    f.detectChanges();
    expect(el.querySelector('.queue-list li .decision-badge')!.textContent).toContain('Unsuitable');
    expect(el.textContent).toContain('Saved: Unsuitable');

    ([...el.querySelectorAll('button')].find(b => /Next/.test(b.textContent!)) as HTMLButtonElement).click();
    http.expectOne('/api/sites/2').flush(detail(item(2, { screeningDecision: 'Promising' })));
    f.detectChanges();
    TestBed.tick();
    http.expectOne(r => r.url === '/api/screening' && r.params.get('siteId') === 'S-2')
      .flush({ siteId: 'S-2', decision: 'Promising', evidence: 'Yard', updatedAtUtc: null, history: [] });
    f.detectChanges();
    expect(el.querySelector('.scr-imagery h2')!.textContent).toContain('2 Bayway');
    http.verify();
  });

  it('shows progress per segment, and choosing a segment scopes the queue and the counts', () => {
    const f = render();
    const el: HTMLElement = f.nativeElement;
    const tiles = [...el.querySelectorAll('.seg-prog')] as HTMLButtonElement[];
    expect(tiles.length).toBe(6);
    expect(tiles[2].querySelector('.sp-top b')!.textContent).toBe('Seg 3');
    expect(tiles[2].querySelector('.sp-top span')!.textContent).toBe('1/25');

    tiles[2].click();
    const req = http.expectOne(r => r.url === '/api/sites');
    expect(req.request.params.getAll('segments')).toEqual(['3']);
    req.flush(page([]));
    f.detectChanges();
    expect(el.querySelector('.scr-scope')!.textContent!.replace(/\s+/g, ' ')).toContain('Segment 3');
    expect(el.querySelector('.scr-scope')!.textContent!.replace(/\s+/g, ' ')).toContain('1 of 25 reviewed (4%)');
    http.verify();
  });

  it('offers queue and screened-results CSV exports that follow the current scope', () => {
    const f = render();
    const links = [...f.nativeElement.querySelectorAll('a')] as HTMLAnchorElement[];
    const queue = links.find(a => /Export queue CSV/.test(a.textContent!))!.getAttribute('href')!;
    const screened = links.find(a => /Export screened sites/.test(a.textContent!))!.getAttribute('href')!;
    expect(queue).toContain('/api/sites/export?');
    expect(queue).toContain('topN=25');
    expect(screened).toContain('screeningDecisions=promising&screeningDecisions=research&screeningDecisions=unsuitable');
    expect(screened).toContain('topN=25');
  });

  it('bookmarks the site under review', () => {
    const f = render();
    const btn = f.nativeElement.querySelector('.scr-imagery .save-btn') as HTMLButtonElement;
    expect(btn.textContent).toContain('☆ Save');
    btn.click();
    const req = http.expectOne(r => r.method === 'POST' && r.url === '/api/saved-sites');
    expect(req.request.body).toEqual({ siteId: 'S-1' });
    req.flush({ id: 7, siteId: 'S-1' });
    f.detectChanges();
    expect(btn.textContent).toContain('★ Saved');
    http.verify();
  });
});
