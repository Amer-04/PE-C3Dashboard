import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { IOS_LABELS, IosClassification, ScoreLine } from '../../core/models';
import { SiteStore } from '../../core/site-store';
import { MethodologyStore } from '../../core/methodology-store';
import { CustomValuesPanelComponent } from '../methodology/custom-values-panel.component';
import { SavedStore } from '../../core/saved-store';
import { CompareStore } from '../../core/compare-store';
import { ContactsPanelComponent } from '../contacts/contacts-panel.component';
import { FeasibilityPanelComponent } from '../feasibility/feasibility-panel.component';
import { scoreCss } from '../../shared/score-color';
import { websiteLink } from '../../shared/external-link';
import { CompareToggleComponent } from '../compare/compare-toggle.component';
import { ScreeningPanelComponent } from '../screening/screening-panel.component';
import { SiteSummaryComponent } from './site-summary.component';

@Component({
  selector: 'app-site-detail',
  imports: [DecimalPipe, CurrencyPipe, RouterLink, ContactsPanelComponent, FeasibilityPanelComponent, CompareToggleComponent, ScreeningPanelComponent, SiteSummaryComponent, CustomValuesPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (store.selected(); as d) {
      <aside class="panel detail" aria-label="Site details">
        <div class="detail-head">
          <div>
            <div class="eyebrow">Segment {{ d.summary.segment }} · rank #{{ d.summary.rank }} of {{ d.sitesInSegment | number }}</div>
            <h2>{{ d.summary.address }}</h2>
            <div class="muted">{{ d.summary.municipality }}, {{ d.summary.county }} County · {{ d.summary.siteId }}</div>
          </div>
          <button type="button" class="icon-btn" aria-label="Close details" (click)="store.select(null)">✕</button>
        </div>

        <div class="score-hero">
          <span class="score-swatch" [style.background]="color(d.summary.score)"></span>
          <span class="score-big">{{ d.summary.score | number: '1.1-1' }}</span>
          <span class="muted">/ ~{{ methodology.maxPossible() | number: '1.0-0' }} · truck demand {{ d.truckDemandPoints | number: '1.1-1' }} / {{ methodology.demandMax() | number: '1.0-0' }}</span>
          @if (d.methodologyVersionId) {
            <a class="method-chip" routerLink="/methodology" [title]="'Scores from methodology ' + (d.methodologyVersionName ?? '')">v{{ d.methodologyVersionId }}</a>
          }
        </div>

        <div class="flags">
          @if (d.summary.freight) { <span class="flag good">✓ Trucking on site</span> }
          @if (d.summary.inFloodZone) { <span class="flag bad">⚠ Flood zone {{ d.floodZone100Yr }}</span> }
          @if (!d.summary.floodChecked) { <span class="flag neutral">? Flood not checked</span> }
          @if (d.summary.isContaminated) { <span class="flag bad">⚠ {{ d.knownContaminatedSites }} known contaminated site(s)</span> }
          @if (d.summary.inHighlandsPreservation) { <span class="flag bad">⚠ Highlands Preservation</span> }
          @if (d.busyMultiTenant) { <span class="flag warn">! Busy multi-tenant</span> }
          @if (d.summary.nearPowerEdison) {
            <span class="flag pe">● {{ d.summary.milesToPowerEdison | number: '1.1-1' }} mi from {{ d.summary.nearestPowerEdisonSite }}</span>
          }
        </div>

        <div class="save-row">
          <button type="button" class="btn save-btn" [class.on]="saved.isSaved(d.summary.siteId)"
                  [disabled]="saved.isBusy(d.summary.siteId)" (click)="saved.toggle(d.summary.siteId)"
                  [attr.aria-pressed]="saved.isSaved(d.summary.siteId)">
            {{ saved.isSaved(d.summary.siteId) ? '★ Saved' : '☆ Save site' }}
          </button>
          @if (saved.refFor(d.summary.siteId); as r) {
            <a class="btn-ghost small" [routerLink]="['/saved', r.id]">Open ↗</a>
          }
          <app-compare-toggle [item]="{ siteId: d.summary.siteId, label: d.summary.address, segment: d.summary.segment }" />
          @if (compare.canCompare()) { <a class="btn-ghost small" routerLink="/compare">Compare {{ compare.count() }} →</a> }
        </div>

        <div class="link-row">
          <a class="btn" [href]="d.satelliteUrl" target="_blank" rel="noopener">Satellite ↗</a>
          <a class="btn" [href]="d.googleMapsUrl" target="_blank" rel="noopener">Google Maps ↗</a>
          <a class="btn" [href]="d.streetViewUrl" target="_blank" rel="noopener">Street View ↗</a>
          @if (website(); as w) { <a class="btn" [href]="w.href" target="_blank" rel="noopener noreferrer" [title]="w.href">Business site ↗</a> }
        </div>

        <app-site-summary [summary]="d.suitability" [validation]="d.validation" />

        <h3>Score breakdown</h3>
        <ul class="breakdown">
          @for (l of positive(); track l.key) {
            <li [title]="l.basis">
              <div class="bd-top"><span>{{ l.label }}</span><b>{{ l.points | number: '1.1-1' }}<small> / {{ l.max }}</small></b></div>
              <div class="bd-track"><div class="bd-fill" [class.neg]="l.points < 0" [style.width.%]="width(l)"></div></div>
              <div class="bd-basis">{{ l.basis }}</div>
            </li>
          }
        </ul>
        @if (penalties().length) {
          <h4>Penalties applied</h4>
          <ul class="breakdown">
            @for (l of penalties(); track l.key) {
              <li><div class="bd-top"><span>{{ l.label }}</span><b class="neg-text">{{ l.points | number: '1.0-0' }}</b></div>
                <div class="bd-basis">{{ l.basis }}</div></li>
            }
          </ul>
        }

        <h3>Site</h3>
        <dl class="facts">
          <dt>Property class</dt><dd>{{ d.summary.propClass }}</dd>
          <dt>Area</dt><dd>{{ d.summary.acres | number: '1.0-2' }} ac ({{ d.summary.openAcres | number: '1.0-2' }} open)</dd>
          <dt>Parking (prelim.)</dt>
          <dd>
            @if (d.summary.estimatedParking !== null) {
              ~{{ d.summary.estimatedParking | number }} trailers <small class="muted">— preliminary screening estimate, not verified</small>
            } @else { <span class="muted">Unknown (no open-acreage estimate)</span> }
          </dd>
          <dt>Lots</dt><dd>{{ d.lotsInSite }} · block {{ d.block }} lot {{ shorten(d.lot) }}</dd>
          <dt>Lot shape</dt><dd>short side {{ d.shortSideFt | number: '1.0-0' }} ft · compactness {{ d.compactness | number: '1.2-2' }}</dd>
          <dt>Building</dt><dd>{{ d.buildingDescription || '—' }}{{ d.yearBuilt ? ' (' + d.yearBuilt + ')' : '' }} · {{ d.buildingCoverage * 100 | number: '1.0-0' }}% coverage</dd>
          <dt>Exit</dt><dd>{{ d.summary.milesToExit | number: '1.2-2' }} mi · {{ d.summary.nearestHighway }} exit {{ d.summary.nearestExit }}</dd>
          <dt>Utility</dt><dd>{{ d.summary.utility || '—' }}</dd>
          <dt>Assessed</dt><dd>{{ d.landValue + d.improvementValue | currency: 'USD' : 'symbol' : '1.0-0' }} · tax {{ d.annualTax | currency: 'USD' : 'symbol' : '1.0-0' }}/yr</dd>
          <dt>Last sale</dt><dd>{{ d.lastSalePrice ? (d.lastSalePrice | currency: 'USD' : 'symbol' : '1.0-0') : '—' }} {{ d.lastDeedDate ? '(deed ' + d.lastDeedDate + ')' : '' }}</dd>
        </dl>

        <h3>Site evaluation</h3>
        <dl class="facts">
          <dt>IOS</dt>
          <dd><span class="ev-badge" [attr.data-ios]="d.summary.iosClassification">{{ iosLabel(d.summary.iosClassification) }}</span>
            <span class="basis-tag" [attr.data-basis]="d.summary.iosBasis">{{ d.summary.iosBasis === 'ManualReview' ? 'Manual review' : 'Source data — not verified' }}</span>
            <br /><small class="muted">{{ d.iosEvidence }}</small>
            @if (d.summary.iosBasis === 'ManualReview') {
              <br /><small class="muted">Source data alone: {{ iosLabel(d.iosDerivedClassification) }}</small>
            }</dd>
          <dt>Truck entrance</dt>
          <dd><span class="ev-badge" [attr.data-v]="badge(d.truckEntranceStatus, d.truckEntranceEvidenceState)">{{ statusLabel(d.truckEntranceStatus, d.truckEntranceEvidenceState) }}</span>
            @if (d.truckEntranceEvidence) { <br /><small class="muted">{{ d.truckEntranceEvidence }}</small> }</dd>
          <dt>Electrical</dt>
          <dd><span class="ev-badge" [attr.data-v]="badge(d.electricalStatus, d.electricalEvidenceState)">{{ statusLabel(d.electricalStatus, d.electricalEvidenceState) }}</span>
            @if (d.electricalEvidence) { <br /><small class="muted">{{ d.electricalEvidence }}</small> }</dd>
          <dt>Verified parking</dt>
          <dd>@if (d.parkingStatus !== 'Unknown') {
              <span class="ev-badge" [attr.data-v]="badge(d.parkingStatus, d.parkingEvidenceState)">{{ statusLabel(d.parkingStatus, d.parkingEvidenceState) }}</span>
              @if (d.verifiedParkingEvidence) { <small class="muted"> — {{ d.verifiedParkingEvidence }}</small> }
            } @else { <span class="muted">Not verified</span> }</dd>
          <dt>Decision-maker</dt>
          <dd>@if (d.verifiedContactCount > 0) { {{ d.verifiedContactCount }} verified contact(s) }
            @else if (d.totalContactCount > 0) { {{ d.totalContactCount }} potential (unverified) }
            @else { <span class="muted">None identified yet</span> }</dd>
        </dl>

        <h3>Truck demand</h3>
        <p class="small">{{ d.demandSummary }}</p>
        <dl class="facts">
          <dt>Trucks based</dt><dd>{{ d.trucksBased3Mi | number }} / {{ d.summary.trucksBased5Mi | number }} / {{ d.trucksBased10Mi | number }} (3/5/10 mi)</dd>
          <dt>Tractor-trailers/day</dt><dd>{{ d.truckAadtAdjacent | number }} adjacent · {{ d.truckAadtAtExit | number }} at exit</dd>
          <dt>Nearest DCFC</dt><dd>{{ d.milesToAnyDcfc | number: '1.1-1' }} mi — {{ d.nearestDcfc }}</dd>
          <dt>Nearest MHD charger</dt><dd>{{ d.milesToTruckDcfc | number: '1.1-1' }} mi</dd>
        </dl>

        <h3>Who to contact</h3>
        <dl class="facts">
          <dt>Businesses</dt><dd>{{ d.summary.businesses || 'None listed' }}</dd>
          @if (d.businessPhone) { <dt>Phone</dt><dd><a [href]="'tel:' + d.businessPhone">{{ d.businessPhone }}</a></dd> }
          <dt>Owner</dt><dd>{{ d.ownerName }}</dd>
          <dt>Owner mail to</dt><dd>{{ d.ownerMailAddress }}, {{ d.ownerMailCity }} {{ d.ownerMailZip }}{{ d.ownerOccupiedHint ? ' (likely owner-occupied)' : '' }}</dd>
        </dl>

        <app-screening-panel [siteId]="d.summary.siteId" (saved)="store.refreshSelected()" />
        <a class="btn-ghost small" [routerLink]="['/screening']" [queryParams]="{ site: d.summary.siteId }">Open in satellite screening →</a>

        <app-contacts-panel [siteId]="d.summary.siteId" (changed)="store.refreshDetail()" />

        <app-feasibility-panel [siteId]="d.summary.siteId" (changed)="store.refreshDetail()" />
        <app-custom-values-panel [siteId]="d.summary.siteId" (changed)="store.refreshDetail()" />

        @if (d.nearbyFleets.length) {
          <h3>Nearby carriers within 5 mi</h3>
          <p class="hint">Carriers located near the site — a demand signal, <b>not confirmed to operate on this property</b>. Research an on-site decision-maker above before outreach.</p>
          <div class="table-scroll">
            <table class="mini">
              <thead><tr><th>Carrier</th><th class="num">Trucks</th><th class="num">mi</th><th>Contact</th></tr></thead>
              <tbody>
                @for (f of d.nearbyFleets; track f.usDot ?? f.carrier) {
                  <tr>
                    <td><b>{{ f.carrier }}</b>@if (f.dba) {<br /><small>{{ f.dba }}</small>}<br /><small class="muted">{{ f.cargo }}</small></td>
                    <td class="num">{{ f.trucks | number }}</td>
                    <td class="num">{{ f.approxMiles | number: '1.1-1' }}</td>
                    <td>@if (f.phone) {<a [href]="'tel:' + f.phone">{{ f.phone }}</a>}@if (f.email) {<br /><a [href]="'mailto:' + f.email">{{ f.email }}</a>}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </aside>
    }
  `,
})
export class SiteDetailComponent {
  protected readonly store = inject(SiteStore);
  protected readonly methodology = inject(MethodologyStore);
  protected readonly saved = inject(SavedStore);
  protected readonly compare = inject(CompareStore);
  /** Listed business website as a safe link (same helper as the ranked table); null when not a usable address. */
  protected readonly website = computed(() => websiteLink(this.store.selected()?.businessWebsite));
  protected readonly positive = computed(() => (this.store.selected()?.scoreLines ?? []).filter(l => l.max > 0));
  protected readonly penalties = computed(() => (this.store.selected()?.scoreLines ?? []).filter(l => l.max === 0 && l.points < 0));
  protected readonly color = scoreCss;

  protected width(l: ScoreLine) {
    return l.points < 0 ? Math.min(100, (100 * -l.points) / Math.abs(l.min)) : Math.min(100, (100 * l.points) / l.max);
  }

  protected shorten(v: string | null) { return v && v.length > 40 ? v.slice(0, 40) + '…' : v; }
  protected iosLabel(c: IosClassification) { return IOS_LABELS[c] ?? c; }
  /** A Verified verdict without evidence text, source URL and evidence date is shown as "Insufficient evidence". */
  protected statusLabel(s: string, state?: string) {
    if (state === 'InsufficientEvidence') return 'Insufficient evidence';
    return s === 'Unknown' ? 'Not reviewed' : s === 'Conflicting' ? 'Conflicting evidence' : s;
  }
  protected badge(s: string, state?: string) { return state === 'InsufficientEvidence' ? 'Insufficient' : s; }
}
