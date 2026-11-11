import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, effect, inject, input, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { MAPS_LOADER } from '../../core/maps-loader';

type UnavailableReason = 'not-configured' | 'rejected' | 'failed';

/**
 * Embedded Google satellite (hybrid) view of one candidate, zoomed to parcel level with a pin on the site.
 * Falls back to a message + external links when the Maps API key is missing or Maps fails to load.
 */
@Component({
  selector: 'app-satellite-map',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sat-wrap">
      <div #mapEl class="sat-map" role="img" [attr.aria-label]="'Satellite view of ' + label()"></div>
      @if (state() === 'unavailable') {
        <div class="sat-fallback">
          <p><b>Satellite imagery unavailable in-app</b> ({{ reasonText[reason()] }}).</p>
          <p class="muted">Use the Satellite / Street View buttons to review the site in Google Maps.</p>
        </div>
      }
      <div class="sat-zoom">
        <button type="button" class="btn-ghost small" (click)="zoomTo(17)" title="Neighbourhood">Area</button>
        <button type="button" class="btn-ghost small" (click)="zoomTo(19)" title="Parcel">Parcel</button>
        <button type="button" class="btn-ghost small" (click)="toggleLabels()">{{ labels() ? 'Hide labels' : 'Show labels' }}</button>
      </div>
    </div>
  `,
})
export class SatelliteMapComponent {
  private readonly api = inject(ApiService);
  private readonly maps = inject(MAPS_LOADER);
  private readonly mapEl = viewChild.required<ElementRef<HTMLDivElement>>('mapEl');

  readonly lat = input.required<number>();
  readonly lng = input.required<number>();
  readonly label = input('candidate site');

  protected readonly state = signal<'loading' | 'ready' | 'unavailable'>('loading');
  protected readonly reason = signal<UnavailableReason>('failed');
  protected readonly reasonText: Record<UnavailableReason, string> = {
    'not-configured': 'no Google Maps key is configured for this deployment',
    rejected: "Google Maps did not accept this site's key",
    failed: 'Google Maps failed to load',
  };
  protected readonly labels = signal(true);
  private map?: google.maps.Map;
  private marker?: google.maps.marker.AdvancedMarkerElement;

  constructor() {
    inject(DestroyRef).onDestroy(this.maps.onAuthFailure(() => this.unavailable('rejected')));
    this.init();
    effect(() => {
      const pos = { lat: this.lat(), lng: this.lng() };
      if (this.state() !== 'ready' || !this.map) return;
      this.map.setCenter(pos);
      this.map.setZoom(19);
      if (this.marker) this.marker.position = pos;
    });
  }

  protected zoomTo(z: number) { this.map?.setZoom(z); this.map?.setCenter({ lat: this.lat(), lng: this.lng() }); }
  protected toggleLabels() {
    this.labels.update(v => !v);
    this.map?.setMapTypeId(this.labels() ? 'hybrid' : 'satellite');
  }

  private unavailable(reason: UnavailableReason) {
    this.reason.set(reason);
    this.state.set('unavailable');
  }

  private async init() {
    try {
      const cfg = await firstValueFrom(this.api.mapsConfig());
      if (!cfg.apiKey) { this.unavailable('not-configured'); return; }
      const { Map } = await this.maps.load(cfg.apiKey);
      const { AdvancedMarkerElement } = await this.maps.marker();
      if (this.state() === 'unavailable') return; // the key was rejected while loading
      const center = { lat: this.lat(), lng: this.lng() };
      this.map = new Map(this.mapEl().nativeElement, {
        center, zoom: 19, mapId: cfg.mapId || 'DEMO_MAP_ID', mapTypeId: 'hybrid', tilt: 0,
        streetViewControl: true, mapTypeControl: false, fullscreenControl: true, gestureHandling: 'greedy',
      });
      this.marker = new AdvancedMarkerElement({ map: this.map, position: center, title: this.label() });
      if (this.state() === 'loading') this.state.set('ready');
    } catch (e) {
      console.error('Satellite map failed to load.', e);
      this.unavailable('failed');
    }
  }
}
