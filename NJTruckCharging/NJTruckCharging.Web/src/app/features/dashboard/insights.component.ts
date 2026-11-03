import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Bucket } from '../../core/models';
import { SiteStore } from '../../core/site-store';

/** Segment cards + distribution charts for the filtered sites. */
@Component({
  selector: 'app-insights',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (store.summary(); as s) {
      <section class="segments" aria-label="Segments">
        @for (seg of s.segments; track seg.segment) {
          <div class="seg-card" [class.on]="store.filter().segments.includes(seg.segment)" [class.empty]="seg.sites === 0">
            <button type="button" class="seg-main" [attr.aria-pressed]="store.filter().segments.includes(seg.segment)"
                    [title]="'Filter to segment ' + seg.segment" (click)="store.toggleSegment(seg.segment)">
              <div class="seg-top"><span class="seg-num">Segment {{ seg.segment }}</span>
                @if (seg.powerEdisonSites && seg.powerEdisonSites !== 'none') { <span class="pe-tag" title="Power Edison sites">● {{ seg.powerEdisonSites }}</span> }
              </div>
              <div class="seg-counties">{{ seg.counties }}</div>
              <div class="seg-stats">
                @if (seg.sites > 0) {
                  <span><b>{{ seg.sites | number }}</b> sites</span>
                  <span>top <b>{{ seg.topScore | number: '1.1-1' }}</b></span>
                  <span>avg {{ seg.averageScore | number: '1.0-0' }}</span>
                } @else {
                  <span>No sites match the current filters</span>
                }
              </div>
            </button>
            @if (seg.topSiteAddress) {
              <button type="button" class="seg-best" (click)="store.select(seg.topSiteId)"
                      [title]="'Open ' + seg.topSiteAddress">#1 {{ seg.topSiteAddress }}, {{ seg.topSiteMunicipality }} →</button>
            }
          </div>
        }
      </section>

      <section class="charts">
        <div class="panel chart">
          <h3>Score distribution</h3>
          <p class="chart-sub">Filtered sites by score band</p>
          <div class="vbars" role="img" [attr.aria-label]="histLabel()">
            @for (b of s.scoreHistogram; track b.label) {
              <div class="vbar" [attr.data-tip]="b.label + ': ' + (b.count | number) + ' sites'">
                <div class="vbar-fill" [style.height.%]="pct(b.count, histMax())"></div>
                <span class="vbar-label">{{ b.label.split('–')[0] }}</span>
              </div>
            }
          </div>
        </div>
        <div class="panel chart">
          <h3>Property class</h3>
          <p class="chart-sub">Best class among a site's lots</p>
          <ul class="hbars">
            @for (b of s.propertyClasses; track b.label) {
              <li [attr.data-tip]="b.label + ': ' + (b.count | number)">
                <span class="hb-label">{{ b.label }}</span>
                <span class="hb-track"><span class="hb-fill" [style.width.%]="pct(b.count, max(s.propertyClasses))"></span></span>
                <span class="hb-val">{{ b.count | number }}</span>
              </li>
            }
          </ul>
          <h3 class="mt">Nearest interstate</h3>
          <ul class="hbars">
            @for (b of s.highways.slice(0, 6); track b.label) {
              <li [attr.data-tip]="b.label + ': ' + (b.count | number)">
                <span class="hb-label">{{ b.label }}</span>
                <span class="hb-track"><span class="hb-fill" [style.width.%]="pct(b.count, max(s.highways))"></span></span>
                <span class="hb-val">{{ b.count | number }}</span>
              </li>
            }
          </ul>
        </div>
        <div class="panel chart">
          <h3>Electric utility</h3>
          <p class="chart-sub">Who you'd file the interconnection with</p>
          <ul class="hbars">
            @for (b of s.utilities.slice(0, 6); track b.label) {
              <li [attr.data-tip]="b.label + ': ' + (b.count | number)">
                <span class="hb-label">{{ shortUtility(b.label) }}</span>
                <span class="hb-track"><span class="hb-fill" [style.width.%]="pct(b.count, max(s.utilities))"></span></span>
                <span class="hb-val">{{ b.count | number }}</span>
              </li>
            }
          </ul>
          <div class="deadline">
            <span class="kpi-value">{{ s.daysToProposalDeadline }}</span>
            <span>days until C3 proposals are due ({{ s.proposalDeadline }})</span>
          </div>
        </div>
      </section>
    }
  `,
})
export class InsightsComponent {
  protected readonly store = inject(SiteStore);
  protected readonly histMax = computed(() => this.max(this.store.summary()?.scoreHistogram ?? []));
  protected readonly histLabel = computed(() =>
    (this.store.summary()?.scoreHistogram ?? []).map(b => `${b.label}: ${b.count}`).join(', '));

  protected max(b: Bucket[]) { return Math.max(1, ...b.map(x => x.count)); }
  protected pct(n: number, m: number) { return n === 0 ? 0 : Math.max(1.5, (100 * n) / m); }
  protected shortUtility(u: string) {
    return u.replace('Public Service Electric & Gas Co.', 'PSE&G').replace('Jersey Central Power & Light', 'JCP&L')
      .replace('Atlantic City Electric', 'Atlantic City Electric').replace('Rockland Electric Company', 'Rockland Electric');
  }
}
