import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { ComparisonPage, Mover, PreviewSummary } from '../../core/methodology.models';

type View = 'overview' | 'segments' | 'criteria' | 'rankings';

/** Proposed vs active scores and rankings for all sites — computed by the server, never applied until published. */
@Component({
  selector: 'app-preview-panel',
  imports: [DecimalPipe, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let p = preview();
    <section class="panel preview" aria-labelledby="pv-title">
      <div class="mm-section-head">
        <div>
          <h3 id="pv-title">Review results: “{{ p.versionName }}” vs active “{{ p.comparedToName }}” (v{{ p.comparedToVersionId }})</h3>
          <p>Recalculated {{ p.sites | number }} sites and re-ranked every segment in {{ p.elapsedMs | number }} ms
            · {{ p.computedAtUtc | date: 'medium' }}. <b>Live scores are unchanged until you apply scoring.</b></p>
        </div>
      </div>
      @if (p.identicalToActive) { <p class="mm-note good">These rules are identical to the active methodology — publishing would change nothing.</p> }

      <div class="stats">
        <div><b>{{ p.scoresChanged | number }}</b><span>scores change</span></div>
        <div><b>{{ p.ranksChanged | number }}</b><span>ranks change</span></div>
        <div><b class="up">+{{ p.maxIncrease | number: '1.1-1' }}</b><span>largest increase</span></div>
        <div><b class="down">{{ p.maxDecrease | number: '1.1-1' }}</b><span>largest decrease</span></div>
        <div><b>{{ p.meanAbsChange | number: '1.2-2' }}</b><span>average change (±)</span></div>
      </div>

      <div class="mm-subtabs" role="tablist" aria-label="Preview details">
        @for (v of views; track v.id) {
          <button type="button" role="tab" [class.on]="view() === v.id" [attr.aria-selected]="view() === v.id" (click)="view.set(v.id)">{{ v.label }}</button>
        }
      </div>

      @switch (view()) {
        @case ('overview') {
          @if (p.changes.length) {
            <h4>Rule changes vs active</h4>
            <ul class="changes">
              @for (c of p.changes; track c.key) {
                <li><span class="pill" [class.green]="c.change === 'Added' || c.change === 'Enabled'" [class.red]="c.change === 'Removed' || c.change === 'Disabled'"
                          [class.blue]="c.change === 'Modified'">{{ c.change }}</span> <b>{{ c.name }}</b>
                  @for (d of c.details; track $index) { <div class="small muted">{{ d }}</div> }</li>
              }
            </ul>
          }
          <div class="movers">
            <div>
              <h4>Biggest risers</h4>
              @for (m of p.topRisers; track m.siteId) { <div class="mv"><span class="pill green">#{{ m.activeRank }} → #{{ m.proposedRank }}</span> S{{ m.segment }} · {{ m.address }}, {{ m.municipality }}</div> }
              @empty { <span class="muted small">none</span> }
            </div>
            <div>
              <h4>Biggest fallers</h4>
              @for (m of p.topFallers; track m.siteId) { <div class="mv"><span class="pill red">#{{ m.activeRank }} → #{{ m.proposedRank }}</span> S{{ m.segment }} · {{ m.address }}, {{ m.municipality }}</div> }
              @empty { <span class="muted small">none</span> }
            </div>
          </div>
        }
        @case ('segments') {
          <h4>Top {{ p.topN }} per segment</h4>
          <div class="scroll">
            <table class="mini">
              <thead><tr><th>Seg</th><th class="num">Sites</th><th class="num">Avg score (active → proposed)</th><th class="num">Ranks changed</th>
                <th class="num">Top {{ p.topN }} kept</th><th>Entering</th><th>Leaving</th></tr></thead>
              <tbody>
                @for (s of p.segments; track s.segment) {
                  <tr>
                    <td>{{ s.segment }}</td><td class="num">{{ s.sites | number }}</td>
                    <td class="num">{{ s.avgActive | number: '1.1-1' }} → {{ s.avgProposed | number: '1.1-1' }}</td>
                    <td class="num">{{ s.ranksChanged | number }}</td><td class="num">{{ s.topNKept }} / {{ p.topN }}</td>
                    <td>@for (m of s.entering; track m.siteId) { <div class="small">#{{ m.proposedRank }} {{ m.address }} <span class="muted">(was #{{ m.activeRank }})</span></div> } @empty { <span class="muted">—</span> }</td>
                    <td>@for (m of s.leaving; track m.siteId) { <div class="small">{{ m.address }} <span class="muted">#{{ m.activeRank }} → #{{ m.proposedRank }}</span></div> } @empty { <span class="muted">—</span> }</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
        @case ('criteria') {
          <h4>Rule impact (proposed)</h4>
          <div class="scroll">
            <table class="mini">
              <thead><tr><th>Criterion</th><th class="num">Avg pts</th><th class="num">Min</th><th class="num">Max</th><th class="num">Sites earning ≠ 0</th><th>Missing data</th></tr></thead>
              <tbody>
                @for (c of p.criteria; track c.key) {
                  <tr [class.off]="!c.enabled">
                    <td>{{ c.name }} @if (!c.enabled) { <span class="muted">(off)</span> }</td>
                    <td class="num">{{ c.avgPoints | number: '1.2-2' }}</td><td class="num">{{ c.minPoints | number: '1.0-2' }}</td>
                    <td class="num">{{ c.maxPoints | number: '1.0-2' }}</td><td class="num">{{ c.sitesNonZero | number }}</td>
                    <td>@if (c.missingPolicy) { <b>{{ c.sitesMissingData | number }}</b> sites — {{ c.missingPolicy }} } @else { <span class="muted">complete</span> }</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }

      <!-- Rankings stay rendered (hidden when another view is open) so paging/search state is kept. -->
      <div [hidden]="view() !== 'rankings'">
        <h4>Proposed rankings</h4>
        <div class="toolbar">
          <select class="mm-ctl" (change)="segment.set(+val($event) || null); page.set(1)" aria-label="Segment">
            <option value="">All segments</option>
            @for (s of p.segments; track s.segment) { <option [value]="s.segment">Segment {{ s.segment }}</option> }
          </select>
          <select class="mm-ctl" (change)="sort.set(val($event)); page.set(1)" aria-label="Sort">
            <option value="proposed">Proposed rank</option><option value="active">Active rank</option>
            <option value="rise">Biggest rise</option><option value="fall">Biggest fall</option><option value="change">Largest score change</option>
          </select>
          <input class="mm-ctl" type="search" placeholder="Site ID, address or town" aria-label="Search sites" (change)="search.set(val($event)); page.set(1)" />
          @if (rows(); as r) {
            <span class="muted small">{{ r.total | number }} sites · page {{ r.page }} / {{ pages() }}</span>
            <button type="button" class="btn small" [disabled]="page() <= 1" (click)="page.set(page() - 1)">‹ Prev</button>
            <button type="button" class="btn small" [disabled]="page() >= pages()" (click)="page.set(page() + 1)">Next ›</button>
          }
        </div>
        <div class="scroll">
          <table class="mini rank-table">
            <thead><tr><th>Seg</th><th>Site</th><th class="num">Proposed</th><th class="num">Active</th><th class="num">Δ score</th><th class="num">Δ rank</th><th class="num">Original (v1)</th></tr></thead>
            <tbody>
              @for (m of rows()?.rows ?? []; track m.siteId) {
                <tr>
                  <td>{{ m.segment }}</td>
                  <td><b>{{ m.address }}</b>, {{ m.municipality }} <span class="muted small">{{ m.siteId }}</span></td>
                  <td class="num"><b>#{{ m.proposedRank }}</b> · {{ m.proposedScore | number: '1.1-1' }}</td>
                  <td class="num">#{{ m.activeRank }} · {{ m.activeScore | number: '1.1-1' }}</td>
                  <td class="num" [class.up]="delta(m) > 0" [class.down]="delta(m) < 0">{{ delta(m) > 0 ? '+' : '' }}{{ delta(m) | number: '1.1-1' }}</td>
                  <td class="num" [class.up]="m.activeRank > m.proposedRank" [class.down]="m.activeRank < m.proposedRank">
                    {{ m.activeRank > m.proposedRank ? '▲' : m.activeRank < m.proposedRank ? '▼' : '' }}{{ abs(m.activeRank - m.proposedRank) || '—' }}</td>
                  <td class="num muted">@if (m.baselineRank) { #{{ m.baselineRank }} · {{ m.baselineScore | number: '1.1-1' }} }</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    </section>
  `,
  styles: [`
    .preview { display: grid; gap: 12px; }
    .preview h3 { margin: 0; font-size: 15px; }
    .preview h4 { margin: 4px 0 6px; }
    .small { font-size: 12px; }
    .stats { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 10px; }
    .stats div { display: flex; flex-direction: column; padding: 10px 12px; border: 1px solid var(--border); border-radius: 10px; background: var(--surface-2); min-width: 0; }
    .stats b { font-size: 20px; font-variant-numeric: tabular-nums; }
    .stats span { font-size: 12px; color: var(--text-2); }
    .changes { list-style: none; padding: 0; margin: 0; display: grid; gap: 6px; }
    .changes li { padding: 7px 10px; border: 1px solid var(--border); border-radius: 8px; font-size: 13px; }
    .movers { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
    .mv { font-size: 12.5px; padding: 4px 0; border-bottom: 1px solid var(--border); display: flex; gap: 6px; align-items: baseline; flex-wrap: wrap; }
    .toolbar { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; margin-bottom: 8px; }
    .toolbar .mm-ctl { width: auto; }
    .scroll { overflow-x: auto; }
    .up { color: var(--good-text); } .down { color: var(--critical); }
    :host-context([data-theme="dark"]) .down { color: #f87171; }
    tr.off { opacity: .55; }
    .rank-table td { vertical-align: top; }
    @media (max-width: 900px) { .stats { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
    @media (max-width: 640px) { .movers { grid-template-columns: 1fr; } .stats { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  `],
})
export class PreviewPanelComponent {
  private readonly api = inject(ApiService);
  readonly preview = input.required<PreviewSummary>();

  protected readonly views: { id: View; label: string }[] = [
    { id: 'overview', label: 'Overview' }, { id: 'segments', label: 'By segment' }, { id: 'criteria', label: 'Rule impact' }, { id: 'rankings', label: 'All site rankings' },
  ];
  protected readonly view = signal<View>('overview');
  protected readonly segment = signal<number | null>(null);
  protected readonly sort = signal('proposed');
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly rows = signal<ComparisonPage | null>(null);
  protected readonly pages = computed(() => Math.max(1, Math.ceil((this.rows()?.total ?? 0) / 25)));

  constructor() {
    effect(() => {
      const p = this.preview();
      const q = { segment: this.segment(), sort: this.sort(), search: this.search(), page: this.page(), pageSize: 25 };
      untracked(() => this.api.methodologyComparison(p.versionId, q).subscribe({ next: r => this.rows.set(r), error: () => this.rows.set(null) }));
    });
  }

  protected delta(m: Mover) { return Math.round((m.proposedScore - m.activeScore) * 10) / 10; }
  protected abs(n: number) { return Math.abs(n); }
  protected val(e: Event) { return (e.target as HTMLInputElement).value; }
}
