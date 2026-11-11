import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAPS_LOADER, MapsLoader } from '../../core/maps-loader';
import { SatelliteMapComponent } from './satellite-map.component';

/** A stand-in for Google Maps: never contacts Google; `reject()` plays Google's gm_authFailure callback. */
function fakeMaps() {
  const listeners = new Set<() => void>();
  const created: unknown[] = [];
  class FakeMap {
    constructor(_el: HTMLElement, opts: unknown) { created.push(opts); }
    setCenter() {} setZoom() {} setMapTypeId() {}
  }
  class FakeMarker { position: unknown; constructor(o: { position: unknown }) { this.position = o.position; } }
  const loader: MapsLoader & { keys: string[] } = {
    keys: [],
    load: async (key: string) => { loader.keys.push(key); return { Map: FakeMap } as unknown as google.maps.MapsLibrary; },
    marker: async () => ({ AdvancedMarkerElement: FakeMarker }) as unknown as google.maps.MarkerLibrary,
    onAuthFailure: (l: () => void) => { listeners.add(l); return () => listeners.delete(l); },
  };
  return { loader, created, listeners, reject: () => [...listeners].forEach(l => l()) };
}

describe('SatelliteMapComponent', () => {
  let http: HttpTestingController;
  let maps: ReturnType<typeof fakeMaps>;

  beforeEach(() => {
    maps = fakeMaps();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MAPS_LOADER, useValue: maps.loader }],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  async function render(config: { apiKey: string; mapId: string } | 'error') {
    const f = TestBed.createComponent(SatelliteMapComponent);
    f.componentRef.setInput('lat', 40.6);
    f.componentRef.setInput('lng', -74.2);
    f.detectChanges();
    const req = http.expectOne('/api/config/maps');
    if (config === 'error') req.flush('down', { status: 503, statusText: 'Service Unavailable' });
    else req.flush(config);
    await new Promise(r => setTimeout(r)); // let the load → marker → map chain finish
    f.detectChanges();
    return f;
  }
  const fallback = (f: { nativeElement: HTMLElement }) => f.nativeElement.querySelector('.sat-fallback')?.textContent ?? null;

  it('says no key is configured when the API returns an empty key, and never loads Google', async () => {
    const f = await render({ apiKey: '', mapId: '' });
    expect(fallback(f)).toContain('no Google Maps key is configured');
    expect(maps.loader.keys).toEqual([]);
  });

  it('loads the hybrid satellite map with the key from /api/config/maps', async () => {
    const f = await render({ apiKey: 'browser-key', mapId: '' });
    expect(maps.loader.keys).toEqual(['browser-key']);
    expect(maps.created).toEqual([expect.objectContaining({ mapTypeId: 'hybrid', mapId: 'DEMO_MAP_ID', center: { lat: 40.6, lng: -74.2 } })]);
    expect(fallback(f)).toBeNull();
  });

  it('switches to the fallback when Google rejects the key after loading (gm_authFailure)', async () => {
    const f = await render({ apiKey: 'browser-key', mapId: '' });
    maps.reject();
    f.detectChanges();
    expect(fallback(f)).toContain("Google Maps did not accept this site's key");
  });

  it('reports a load failure when the maps settings cannot be fetched', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const f = await render('error');
    expect(fallback(f)).toContain('Google Maps failed to load');
  });

  it('stops listening for key rejection when destroyed', async () => {
    const f = await render({ apiKey: 'browser-key', mapId: '' });
    expect(maps.listeners.size).toBe(1);
    f.destroy();
    expect(maps.listeners.size).toBe(0);
  });
});
