import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { CONTACT_READINESS, ContactReadiness, SCREENING_LABELS, SITE_EVIDENCE, ScreeningDecision, SiteEvidence, SiteSort } from '../../core/models';
import { SiteStore } from '../../core/site-store';
import { SavedStore } from '../../core/saved-store';
import { scoreCss } from '../../shared/score-color';
import { CompareToggleComponent } from '../compare/compare-toggle.component';
import { ContactCellComponent } from './contact-cell.component';
import { BusinessCellComponent } from './business-cell.component';

@Component({
  selector: 'app-sites-table',
  imports: [DecimalPipe, CompareToggleComponent, ContactCellComponent, BusinessCellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel table-panel">
      <div class="panel-head">
        <h2>Ranked sites</h2>
        <div class="head-actions">
          @if (store.table(); as t) { <span class="muted">{{ t.totalCount | number }} sites</span> }
          <a class="btn" [href]="store.exportUrl()" download>Export CSV</a>
          <a class="btn" [href]="store.researchExportUrl()" download
             title="Research shortlist: parcel evidence, verified vs preliminary data, professional contact, sources and what is still missing">Export research shortlist</a>
        </div>
      </div>
      <div class="table-scroll">
        <table class="sites">
          <thead>
            <tr>
              <th class="num">Seg</th>
              <th class="num sortable" (click)="sort('Rank')">Rank{{ arrow('Rank') }}</th>
              <th class="num sortable" (click)="sort('Score')">Score{{ arrow('Score') }}</th>
              <th>Site</th>
              <th>Class</th>
              <th class="num sortable" (click)="sort('OpenAcres')">Open ac{{ arrow('OpenAcres') }}</th>
              <th class="num sortable" (click)="sort('MilesToExit')">Exit mi{{ arrow('MilesToExit') }}</th>
              <th class="num sortable" (click)="sort('TrucksBased5Mi')">Trucks 5 mi{{ arrow('TrucksBased5Mi') }}</th>
              <th class="num sortable" (click)="sort('TruckTraffic')">Trucks/day{{ arrow('TruckTraffic') }}</th>
              <th>Business on site</th>
              <th class="num sortable" (click)="sort('MilesToPowerEdison')">PE site mi{{ arrow('MilesToPowerEdison') }}</th>
              <th>Flags</th>
              <th>Screening</th>
              <th title="From the team's research records only: parcel check, business at address, screening">Site evidence</th>
              <th title="From researched contacts only; a general company line is not a site decision-maker">Contact readiness</th>
              <th>Save</th>
              <th>Compare</th>
            </tr>
          </thead>
          <tbody>
            @for (s of store.table()?.items ?? []; track s.id) {
              <tr [class.selected]="store.selectedId() === s.id" (click)="open(s.id)" tabindex="0"
                  (keydown.enter)="open(s.id)">
                <td class="num">{{ s.segment }}</td>
                <td class="num">#{{ s.rank }}</td>
                <td class="num"><span class="score-pill"><i [style.background]="color(s.score)"></i>{{ s.score | number: '1.1-1' }}</span></td>
                <td><b>{{ s.address }}</b><br /><small class="muted">{{ s.municipality }}, {{ s.county }}</small></td>
                <td>{{ s.propClass }}</td>
                <td class="num">{{ s.openAcres | number: '1.1-1' }}</td>
                <td class="num">{{ s.milesToExit | number: '1.2-2' }}<br /><small class="muted">{{ s.nearestHighway }} {{ s.nearestExit }}</small></td>
                <td class="num">{{ s.trucksBased5Mi | number }}</td>
                <td class="num">{{ s.truckTraffic | number }}</td>
                <td class="biz-col"><app-business-cell [site]="s" /></td>
                <td class="num" [class.pe-near]="s.nearPowerEdison">{{ s.milesToPowerEdison | number: '1.1-1' }}</td>
                <td class="flags-cell">
                  @if (s.freight) { <span class="flag good" title="Trucking / logistics business on site">Truck</span> }
                  @if (s.inFloodZone) { <span class="flag bad" title="FEMA 100-yr flood zone">Flood</span> }
                  @if (s.isContaminated) { <span class="flag bad" title="Known contaminated site">Contam.</span> }
                  @if (s.inHighlandsPreservation) { <span class="flag bad" title="Highlands Preservation Area">Highlands</span> }
                  @if (s.nearPowerEdison) { <span class="flag pe" title="Within 2 mi of a Power Edison site">PE ≤2 mi</span> }
                </td>
                <td><span class="decision-badge" [attr.data-d]="s.screeningDecision">{{ decisionLabel(s.screeningDecision) }}</span></td>
                <td><span class="rr-badge" [attr.data-ev]="s.siteEvidence" [title]="evidenceHint(s.siteEvidence)">{{ s.siteEvidence }}</span></td>
                <td class="contact-col" [attr.data-cr]="s.contactReadiness" [title]="readiness(s.contactReadiness).label + ' — ' + readiness(s.contactReadiness).hint"><app-contact-cell [site]="s" /></td>
                <td class="save-cell" (click)="$event.stopPropagation()">
                  <button type="button" class="save-btn" [class.on]="saved.isSaved(s.siteId)"
                          [disabled]="saved.isBusy(s.siteId)"
                          (click)="saved.toggle(s.siteId)"
                          [attr.aria-pressed]="saved.isSaved(s.siteId)"
                          [title]="saved.isSaved(s.siteId) ? 'Remove from saved' : 'Save this site'">
                    {{ saved.isSaved(s.siteId) ? '★ Saved' : '☆ Save' }}
                  </button>
                </td>
                <td class="save-cell" (click)="$event.stopPropagation()">
                  <app-compare-toggle [item]="{ siteId: s.siteId, label: s.address, segment: s.segment }" [compact]="true" />
                </td>
              </tr>
            } @empty {
              <tr><td colspan="17" class="empty">No sites match these filters.</td></tr>
            }
          </tbody>
        </table>
      </div>
      @if (store.table(); as t) {
        <div class="pager">
          <button type="button" class="btn" [disabled]="t.page <= 1" (click)="store.page.set(t.page - 1)">‹ Prev</button>
          <span>Page {{ t.page }} of {{ t.totalPages || 1 }}</span>
          <button type="button" class="btn" [disabled]="t.page >= t.totalPages" (click)="store.page.set(t.page + 1)">Next ›</button>
          <select [value]="store.pageSize()" (change)="setSize($event)" aria-label="Rows per page">
            <option [value]="25">25 / page</option><option [value]="50">50 / page</option><option [value]="100">100 / page</option>
          </select>
        </div>
      }
    </section>
  `,
})
export class SitesTableComponent {
  protected readonly store = inject(SiteStore);
  protected readonly saved = inject(SavedStore);
  protected readonly color = scoreCss;
  protected sort(by: SiteSort) { this.store.sort(by); }
  protected decisionLabel(d: ScreeningDecision) { return SCREENING_LABELS[d] ?? d; }
  protected evidenceHint(e: SiteEvidence) { return SITE_EVIDENCE.find(x => x.value === e)?.hint ?? ''; }
  protected readiness(r: ContactReadiness) { return CONTACT_READINESS.find(x => x.value === r) ?? CONTACT_READINESS[3]; }

  /** Select the site and bring the map + detail panel into view. */
  protected open(id: number) {
    this.store.select(id);
    document.querySelector('.workspace')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
  protected arrow(by: SiteSort) { return this.store.sortBy() === by ? (this.store.descending() ? ' ▾' : ' ▴') : ''; }
  protected setSize(e: Event) { this.store.pageSize.set(+(e.target as HTMLSelectElement).value); this.store.page.set(1); }
}
