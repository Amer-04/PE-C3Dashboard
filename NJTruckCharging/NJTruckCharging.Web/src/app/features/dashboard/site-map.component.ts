import {
  ChangeDetectionStrategy, Component, ElementRef, OnDestroy, afterNextRender, effect, inject, signal, viewChild,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Deck, FlyToInterpolator, Layer, MapViewState, PickingInfo } from '@deck.gl/core';
import { GoogleMapsOverlay } from '@deck.gl/google-maps';
import { BitmapLayer, ScatterplotLayer, TextLayer } from '@deck.gl/layers';
import { TileLayer } from '@deck.gl/geo-layers';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { loadGoogleMaps } from '../../core/maps-loader';
import { PowerEdisonSite, SiteMapPoint } from '../../core/models';
import { SiteStore } from '../../core/site-store';
import { FLOOD_RGB, PE_RGB, RAMP_CSS, SCORE_MAX, SCORE_MIN, scoreRgb } from '../../shared/score-color';

const NJ_CENTER = { lat: 40.42, lng: -74.45 };
const TWO_MILES_M = 3218.69;

interface Hover { x: number; y: number; point?: SiteMapPoint; pe?: PowerEdisonSite; }

@Component({
  selector: 'app-site-map',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="map-wrap">
      <div #mapEl class="map-canvas"></div>

      <div class="map-toolbar">
        @if (mode() === 'google') {
          <div class="seg-toggle" role="group" aria-label="Map type">
            <button type="button" [class.on]="mapType() === 'roadmap'" (click)="setMapType('roadmap')">Map</button>
            <button type="button" [class.on]="mapType() === 'hybrid'" (click)="setMapType('hybrid')">Satellite</button>
          </div>
        }
        <button type="button" class="btn-ghost small" (click)="resetView()">Reset view</button>
      </div>

      <div class="map-legend">
        <div class="legend-title">Site score</div>
        <div class="legend-ramp" [style.background]="ramp"></div>
        <div class="legend-scale"><span>≤{{ scoreMin }}</span><span>{{ scoreMax }}+</span></div>
        <div class="legend-row"><span class="dot pe"></span> EV Edison / Power Edison site (2-mi ring)</div>
        <div class="legend-row"><span class="dot flood"></span> In FEMA 100-yr flood zone</div>
        <div class="legend-count">{{ store.points().length | number }} sites shown</div>
      </div>

      @if (mode() === 'fallback') {
        <div class="map-notice">
          No Google Maps API key configured — showing an open basemap.
          Add <code>GoogleMaps:ApiKey</code> to the API settings (see README).
          <span class="attrib">© OpenStreetMap contributors</span>
        </div>
      }
      @if (store.loading()) { <div class="map-loading">Loading sites…</div> }

      @if (hover(); as h) {
        <div class="map-tooltip" [style.left.px]="h.x + 14" [style.top.px]="h.y + 14">
          @if (h.point; as p) {
            <strong>Score {{ p.score | number: '1.1-1' }}</strong>
            <span>Segment {{ p.segment }} · rank #{{ p.rank | number }}</span>
            <span>{{ p.openAcres | number: '1.0-1' }} open ac{{ p.freight ? ' · trucking on site' : '' }}</span>
            @if (p.flood) { <span class="warn">In 100-yr flood zone</span> }
            @if (p.nearPe) { <span class="pe-note">Within 2 mi of a Power Edison site</span> }
            <em>Click for details</em>
          }
          @if (h.pe; as s) {
            <strong>{{ s.name }}</strong>
            <span>{{ statusLabel(s.status) }} · Segment {{ s.segment }}</span>
            <span>{{ s.candidatesWithinTwoMiles | number }} candidates within 2 mi</span>
          }
        </div>
      }
    </div>
  `,
})
export class SiteMapComponent implements OnDestroy {
  protected readonly store = inject(SiteStore);
  private readonly api = inject(ApiService);
  private readonly mapEl = viewChild.required<ElementRef<HTMLDivElement>>('mapEl');

  protected readonly mode = signal<'loading' | 'google' | 'fallback'>('loading');
  protected readonly mapType = signal<'roadmap' | 'hybrid'>('roadmap');
  protected readonly hover = signal<Hover | null>(null);
  protected readonly ramp = RAMP_CSS;
  protected readonly scoreMin = SCORE_MIN;
  protected readonly scoreMax = SCORE_MAX;

  private map?: google.maps.Map;
  private overlay?: GoogleMapsOverlay;
  private deck?: Deck;

  constructor() {
    afterNextRender(() => void this.init());

    // Re-render layers whenever data or selection changes.
    effect(() => {
      const layers = this.buildLayers(this.store.points(), this.store.powerEdisonSites(), this.store.selectedId());
      this.overlay?.setProps({ layers });
      this.deck?.setProps({ layers: [this.baseTiles(), ...layers] });
    });

    // Fly to the selected site.
    effect(() => {
      const s = this.store.selected()?.summary;
      if (!s) return;
      if (this.map) {
        this.map.panTo({ lat: s.lat, lng: s.lng });
        if ((this.map.getZoom() ?? 0) < 14) this.map.setZoom(15);
      } else if (this.deck) {
        this.deck.setProps({
          initialViewState: {
            longitude: s.lng, latitude: s.lat, zoom: 14.5,
            transitionDuration: 900, transitionInterpolator: new FlyToInterpolator(),
          } as MapViewState,
        });
      }
    });
  }

  private async init() {
    let key = '';
    let mapId = 'DEMO_MAP_ID';
    try {
      const cfg = await firstValueFrom(this.api.mapsConfig());
      key = cfg.apiKey;
      mapId = cfg.mapId || mapId;
    } catch { /* API offline – fall back */ }

    if (key) {
      try {
        const { Map } = await loadGoogleMaps(key);
        this.map = new Map(this.mapEl().nativeElement, {
          center: NJ_CENTER, zoom: 8, mapId,
          mapTypeControl: false, streetViewControl: true, fullscreenControl: true,
          clickableIcons: false, gestureHandling: 'greedy',
        });
        this.overlay = new GoogleMapsOverlay({ interleaved: false, layers: this.currentLayers() });
        this.overlay.setMap(this.map);
        this.mode.set('google');
        return;
      } catch (e) {
        console.error('Google Maps failed to load; using fallback basemap.', e);
      }
    }

    this.deck = new Deck({
      parent: this.mapEl().nativeElement,
      initialViewState: this.homeView(),
      controller: true,
      layers: [this.baseTiles(), ...this.currentLayers()],
      getCursor: ({ isHovering }) => (isHovering ? 'pointer' : 'grab'),
    });
    this.mode.set('fallback');
  }

  protected setMapType(type: 'roadmap' | 'hybrid') {
    this.mapType.set(type);
    this.map?.setMapTypeId(type);
  }

  protected resetView() {
    if (this.map) { this.map.setCenter(NJ_CENTER); this.map.setZoom(8); }
    this.deck?.setProps({ initialViewState: { ...this.homeView(), transitionDuration: 600 } as MapViewState });
  }

  protected statusLabel(s: PowerEdisonSite['status']) {
    return s === 'Operating' ? 'Operating hub' : s === 'LetterOfIntent' ? 'Letter of intent' : 'Planned';
  }

  private homeView(): MapViewState {
    return { longitude: NJ_CENTER.lng, latitude: NJ_CENTER.lat, zoom: 7.6, pitch: 0, bearing: 0 };
  }

  private currentLayers() {
    return this.buildLayers(this.store.points(), this.store.powerEdisonSites(), this.store.selectedId());
  }

  private buildLayers(points: SiteMapPoint[], pe: PowerEdisonSite[], selectedId: number | null): Layer[] {
    // Draw low scores first so the best sites sit on top.
    const sorted = [...points].sort((a, b) => a.score - b.score);
    const selected = points.find(p => p.id === selectedId);
    const onHover = (info: PickingInfo) => {
      const obj = info.object as SiteMapPoint | PowerEdisonSite | undefined;
      if (!obj) { this.hover.set(null); return; }
      this.hover.set('status' in obj ? { x: info.x, y: info.y, pe: obj } : { x: info.x, y: info.y, point: obj });
    };

    return [
      new ScatterplotLayer<PowerEdisonSite>({
        id: 'pe-rings', data: pe, getPosition: d => [d.lng, d.lat],
        radiusUnits: 'meters', getRadius: TWO_MILES_M,
        filled: true, getFillColor: [...PE_RGB, 22], stroked: true, getLineColor: [...PE_RGB, 200],
        lineWidthUnits: 'pixels', getLineWidth: 1.5,
      }),
      new ScatterplotLayer<SiteMapPoint>({
        id: 'sites', data: sorted, pickable: true, autoHighlight: true, highlightColor: [235, 104, 52, 255],
        getPosition: d => [d.lng, d.lat],
        radiusUnits: 'pixels',
        getRadius: d => Math.max(2.5, Math.min(8, 2.5 + (d.score - 40) / 12)),
        radiusMinPixels: 2.5,
        getFillColor: d => [...scoreRgb(d.score), d.score >= 80 ? 245 : 200],
        stroked: true, lineWidthUnits: 'pixels',
        getLineColor: d => (d.flood ? [...FLOOD_RGB, 255] : [255, 255, 255, 230]),
        getLineWidth: d => (d.flood ? 2 : 1),
        onHover,
        onClick: info => { const p = info.object as SiteMapPoint | undefined; if (p) this.store.select(p.id); },
        updateTriggers: { getRadius: points.length, getFillColor: points.length },
      }),
      new ScatterplotLayer<SiteMapPoint>({
        id: 'selected', data: selected ? [selected] : [], getPosition: d => [d.lng, d.lat],
        radiusUnits: 'pixels', getRadius: 13, filled: false, stroked: true,
        getLineColor: [...PE_RGB, 255], lineWidthUnits: 'pixels', getLineWidth: 3,
      }),
      new ScatterplotLayer<PowerEdisonSite>({
        id: 'pe-sites', data: pe, pickable: true, getPosition: d => [d.lng, d.lat],
        radiusUnits: 'pixels', getRadius: 9, getFillColor: [...PE_RGB, 255],
        stroked: true, getLineColor: [255, 255, 255, 255], lineWidthUnits: 'pixels', getLineWidth: 2.5,
        onHover,
      }),
      new TextLayer<PowerEdisonSite>({
        id: 'pe-labels', data: pe, getPosition: d => [d.lng, d.lat], getText: d => d.shortName,
        getSize: 12, getColor: [40, 30, 20, 255], getPixelOffset: [0, -18], fontWeight: 700,
        background: true, getBackgroundColor: [255, 255, 255, 230], backgroundPadding: [5, 2],
        fontFamily: 'Inter, system-ui, sans-serif',
      }),
    ];
  }

  /**
   * Open basemap used only when no Google Maps key is configured. OpenStreetMap's standard tiles need no key
   * but are volunteer-run: fine for local/demo use with attribution, not for heavy production traffic
   * (https://operations.osmfoundation.org/policies/tiles/). CARTO's free tiles now require an API key.
   */
  private baseTiles() {
    return new TileLayer({
      id: 'basemap',
      data: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
      minZoom: 0, maxZoom: 19, tileSize: 256,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      renderSubLayers: (props: any) => {
        const [[west, south], [east, north]] = props.tile.boundingBox;
        return new BitmapLayer(props, { data: undefined, image: props.data, bounds: [west, south, east, north] });
      },
    });
  }

  ngOnDestroy() {
    this.overlay?.finalize();
    this.deck?.finalize();
  }
}
