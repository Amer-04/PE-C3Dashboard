import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { REVIEW_ISSUES, ResearchSummary, ReviewIssue, ReviewQueue, ReviewQueueItem, SegmentResearch } from '../../core/models';
import { ContactsPanelComponent } from '../contacts/contacts-panel.component';
import { FeasibilityPanelComponent } from '../feasibility/feasibility-panel.component';

/**
 * Site research review queue: research-set sites (saved, screened, started or previously researched) with unresolved
 * verification issues, plus research completeness per C3 segment. Issues are resolved by recording documented
 * evidence in the same feasibility / contact panels used everywhere else. Independent of the C3 score; not a CRM.
 */
@Component({
  selector: 'app-research-review',
  imports: [FeasibilityPanelComponent, ContactsPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel rr-top">
      <div class="panel-head">
        <div>
          <h2>Research review</h2>
          <div class="muted small">Unresolved site-verification issues and research completeness (8 core checks). Separate from the C3 score — not an outreach pipeline.</div>
        </div>
      </div>

      @if (summary(); as s) {
        <div class="rr-scope small muted">
          <b>{{ s.researchSites }}</b> sites in research · {{ s.allReviewed }} fully reviewed · {{ s.partlyReviewed }} partly · {{ s.noneReviewed }} not started ·
          checks reviewed <b>{{ s.checksReviewed }}</b>/{{ s.checksTotal }}, verified <b>{{ s.checksVerified }}</b>/{{ s.checksTotal }}
        </div>
        <div class="seg-progress" role="group" aria-label="Research completeness by segment">
          @for (g of segmentsAll(); track g.segment) {
            <button type="button" class="seg-prog rr-seg" [class.on]="segment() === g.segment" (click)="setSegment(segment() === g.segment ? null : g.segment)"
                    [attr.aria-pressed]="segment() === g.segment"
                    [title]="'Segment ' + g.segment + ': ' + g.researchSites + ' in research — ' + g.allReviewed + ' fully reviewed, ' + g.partlyReviewed + ' partly, ' + g.noneReviewed + ' not started; ' + g.checksVerified + ' of ' + g.checksTotal + ' checks verified'">
              <span class="sp-top"><b>Seg {{ g.segment }}</b><span>{{ g.checksReviewed }}/{{ g.checksTotal }} checks</span></span>
              <span class="sp-bar" aria-hidden="true">
                <i class="rv" [style.width.%]="share(g.checksVerified, g.checksTotal)"></i><i class="rc" [style.width.%]="share(g.checksReviewed - g.checksVerified, g.checksTotal)"></i>
              </span>
              <span class="sp-foot muted">{{ g.researchSites }} site{{ g.researchSites === 1 ? '' : 's' }} · {{ g.sitesWithIssues }} with issues</span>
            </button>
          }
        </div>
        <div class="rr-legend small muted"><i class="rv"></i> verified <i class="rc"></i> reviewed with concern <i class="rn"></i> not yet reviewed</div>
      }

      @if (queue(); as q) {
        <div class="chips rr-issues" role="group" aria-label="Filter by issue">
          @for (o of issueOptions; track o.value) {
            <button type="button" class="chip" [class.on]="issues().includes(o.value)" [attr.aria-pressed]="issues().includes(o.value)"
                    [title]="o.hint" (click)="toggleIssue(o.value)">{{ o.label }} <b>{{ q.issueCounts[o.value] ?? 0 }}</b></button>
          }
          @if (issues().length) { <button type="button" class="btn-ghost small" (click)="issues.set([]); reload()">Clear</button> }
        </div>
      }
    </section>

    <section class="panel rr-list" aria-label="Review queue">
      @if (queue(); as q) {
        <div class="panel-head"><h2>Queue</h2>
          <span class="muted small">{{ q.items.length }} site{{ q.items.length === 1 ? '' : 's' }} with open issues
            @if (q.excludedUnsuitable) { · {{ q.excludedUnsuitable }} screened Unsuitable (resolved) }</span></div>
        <div class="table-wrap">
          <table class="sites rr-table">
            <thead><tr><th class="num">Seg</th><th class="num">Rank</th><th>Site</th><th title="Core checks reviewed / verified, out of 8">Completeness</th><th>Open issues</th><th></th></tr></thead>
            <tbody>
              @for (i of rows(); track i.siteId) {
                <tr [class.on]="open() === i.siteId">
                  <td class="num">{{ i.segment }}</td>
                  <td class="num">#{{ i.rank }}</td>
                  <td><b>{{ i.address }}</b><br /><small class="muted">{{ i.municipality }}, {{ i.county }} · {{ i.siteId }}</small></td>
                  <td>
                    <span class="rc-mini" [title]="i.reviewed + ' reviewed, ' + i.verified + ' verified of ' + i.total">
                      <span class="rc-bar"><i class="v" [style.width.%]="share(i.verified, i.total)"></i><i class="c" [style.width.%]="share(i.reviewed - i.verified, i.total)"></i></span>
                      <span>{{ i.reviewed }}/{{ i.total }} reviewed · {{ i.verified }} verified</span>
                    </span>
                  </td>
                  <td><span class="rr-badges">@for (x of i.issues; track x.issue) { <span class="rr-badge" [attr.data-i]="x.issue">{{ x.label }}</span> }
                    @empty { <span class="rr-badge" data-i="Resolved">Resolved — no open issues</span> }</span></td>
                  <td><button type="button" class="btn small" (click)="toggle(i)" [attr.aria-expanded]="open() === i.siteId">{{ open() === i.siteId ? 'Close' : 'Resolve' }}</button></td>
                </tr>
                @if (open() === i.siteId) {
                  <tr class="rr-detail"><td colspan="6">
                    <div class="rr-panels">
                      <app-feasibility-panel [siteId]="i.siteId" (changed)="refresh()" />
                      <app-contacts-panel [siteId]="i.siteId" (changed)="refresh()" />
                    </div>
                  </td></tr>
                }
              } @empty {
                <tr><td colspan="6" class="empty">No open issues {{ segment() ? 'in segment ' + segment() : '' }}{{ issues().length ? ' for the selected issue types' : '' }}.</td></tr>
              }
            </tbody>
          </table>
        </div>
      } @else if (error()) {
        <p class="neg-text small" role="alert">{{ error() }}</p>
      } @else {
        <p class="muted small">Loading…</p>
      }
    </section>
  `,
})
export class ResearchReviewComponent {
  private readonly api = inject(ApiService);

  protected readonly issueOptions = REVIEW_ISSUES;
  protected readonly segment = signal<number | null>(null);
  protected readonly issues = signal<ReviewIssue[]>([]);
  protected readonly queue = signal<ReviewQueue | null>(null);
  protected readonly summary = signal<ResearchSummary | null>(null);
  protected readonly open = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  /** Always show all six segments, even those with nothing in research yet. */
  protected readonly segmentsAll = computed<SegmentResearch[]>(() => {
    const by = new Map((this.summary()?.segments ?? []).map(s => [s.segment, s]));
    return [1, 2, 3, 4, 5, 6].map(n => by.get(n) ?? {
      segment: n, researchSites: 0, noneReviewed: 0, partlyReviewed: 0, allReviewed: 0,
      checksReviewed: 0, checksVerified: 0, checksTotal: 0, sitesWithIssues: 0,
    });
  });

  constructor() {
    this.reload();
    this.loadSummary();
  }

  protected share(n: number, total: number) { return total > 0 ? (100 * n) / total : 0; }

  protected setSegment(seg: number | null) { this.segment.set(seg); this.open.set(null); this.reload(); }

  protected toggleIssue(i: ReviewIssue) {
    const cur = this.issues();
    this.issues.set(cur.includes(i) ? cur.filter(x => x !== i) : [...cur, i]);
    this.reload();
  }

  protected toggle(i: ReviewQueueItem) {
    const opening = this.open() !== i.siteId;
    this.open.set(opening ? i.siteId : null);
    this.pinned.set(opening ? i : null);
  }

  /** The site being resolved, kept on screen (as resolved) after its last issue clears, until it is closed. */
  private readonly pinned = signal<ReviewQueueItem | null>(null);

  /** Queue rows, plus the open site if it has just left the queue. */
  protected readonly rows = computed<ReviewQueueItem[]>(() => {
    const items = this.queue()?.items ?? [];
    const p = this.pinned();
    return p && this.open() === p.siteId && !items.some(i => i.siteId === p.siteId) ? [p, ...items] : items;
  });

  /** After evidence is recorded: refresh the queue and the segment progress. */
  protected refresh() { this.reload(); this.loadSummary(); }

  protected reload() {
    const seg = this.segment();
    this.api.reviewQueue(seg ? [seg] : [], this.issues()).subscribe({
      next: q => {
        const p = this.pinned();
        if (p) {
          const fresh = q.items.find(i => i.siteId === p.siteId);
          // Still queued: track its latest counts. Resolved: keep it, with no issues, until it is closed.
          this.pinned.set(fresh ?? { ...p, issues: [] });
          if (!fresh) this.api.researchSite(p.siteId).subscribe({ next: r => this.pinned.set({ ...p, issues: [], reviewed: r.reviewed, verified: r.verified }), error: () => {} });
        }
        this.queue.set(q);
        this.error.set(null);
      },
      error: () => this.error.set('Could not load the review queue.'),
    });
  }

  private loadSummary() {
    this.api.researchSummary().subscribe({ next: s => this.summary.set(s), error: () => {} });
  }
}
