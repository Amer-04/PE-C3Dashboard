import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { SiteStore } from '../../core/site-store';
import { MethodologyStore } from '../../core/methodology-store';

@Component({
  selector: 'app-kpi-strip',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (store.summary(); as s) {
      <section class="kpis" aria-label="Key figures for the filtered sites">
        <div class="kpi">
          <span class="kpi-label">Candidate sites</span>
          <span class="kpi-value">{{ s.kpis.sites | number }}</span>
          <span class="kpi-sub">of {{ s.kpis.totalSites | number }} screened</span>
        </div>
        <div class="kpi">
          <span class="kpi-label">Top score</span>
          <span class="kpi-value">{{ s.kpis.topScore | number: '1.1-1' }}</span>
          <span class="kpi-sub">average {{ s.kpis.averageScore | number: '1.1-1' }} (max ≈ {{ methodology.maxPossible() | number: '1.0-0' }})</span>
        </div>
        <div class="kpi">
          <span class="kpi-label">Trucking on site</span>
          <span class="kpi-value">{{ s.kpis.freightOnSite | number }}</span>
          <span class="kpi-sub">{{ pct(s.kpis.freightOnSite, s.kpis.sites) }} of filtered sites</span>
        </div>
        <div class="kpi">
          <span class="kpi-label">Within 0.5 mi of exit</span>
          <span class="kpi-value">{{ s.kpis.withinHalfMileOfExit | number }}</span>
          <span class="kpi-sub">median open land {{ s.kpis.medianOpenAcres | number: '1.1-1' }} ac</span>
        </div>
        <div class="kpi">
          <span class="kpi-label">Near a Power Edison site</span>
          <span class="kpi-value">{{ s.kpis.nearPowerEdison | number }}</span>
          <span class="kpi-sub">≤ 2 mi — may compete for funding</span>
        </div>
        <div class="kpi">
          <span class="kpi-label">Flagged</span>
          <span class="kpi-value">{{ s.kpis.inFloodZone + s.kpis.contaminated | number }}</span>
          <span class="kpi-sub">{{ s.kpis.inFloodZone | number }} flood · {{ s.kpis.contaminated | number }} contaminated</span>
        </div>
      </section>
    } @else {
      <section class="kpis skeleton" aria-hidden="true">
        @for (i of [1, 2, 3, 4, 5, 6]; track i) { <div class="kpi"></div> }
      </section>
    }
  `,
})
export class KpiStripComponent {
  protected readonly store = inject(SiteStore);
  protected readonly methodology = inject(MethodologyStore);
  protected pct(n: number, d: number) { return d ? `${Math.round((100 * n) / d)}%` : '—'; }
}
