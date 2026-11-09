import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import {
  CHECK_STATE_LABELS, CORE_CHECK_ITEMS, FEASIBILITY_ITEMS, FeasibilityItem, FeasibilitySummary, SiteResearch,
} from '../../core/models';
import { RecordedByStore } from '../../core/recorded-by';
import { FeasibilityRowComponent } from './feasibility-row.component';

/**
 * Site feasibility review — a ground/records checklist (Verified / Concern / Conflicting / Unknown + evidence,
 * source and date), with research completeness of the eight core checks. Kept visually and functionally separate
 * from the C3 score.
 */
@Component({
  selector: 'app-feasibility-panel',
  imports: [FeasibilityRowComponent, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="feasibility">
      <div class="feas-headrow">
        <h3>Site feasibility review</h3>
        @if (summary(); as s) {
          <span class="feas-progress">{{ s.reviewed }}/{{ s.total }} recorded
            @if (s.concern > 0) { · <span class="neg-text">{{ s.concern }} concern{{ s.concern > 1 ? 's' : '' }}</span> }
          </span>
        }
      </div>
      <p class="hint">A manual verification — <b>independent of the C3 score</b>.</p>

      @if (research(); as r) {
        <div class="rc-box" aria-label="Research completeness">
          <div class="rc-head">
            <b>Research completeness</b>
            <span class="rc-counts">
              <span title="Core checks with a documented verdict (Verified or Concern) — a Concern counts as reviewed">Reviewed <b>{{ r.reviewed }}</b>/{{ r.total }}</span>
              <span title="Core checks successfully verified with documented evidence — a Concern never counts">Verified <b>{{ r.verified }}</b>/{{ r.total }}</span>
            </span>
          </div>
          <div class="rc-bar" role="img" [attr.aria-label]="r.verified + ' verified and ' + (r.reviewed - r.verified) + ' reviewed with concerns of ' + r.total">
            <i class="v" [style.width.%]="100 * r.verified / r.total"></i><i class="c" [style.width.%]="100 * (r.reviewed - r.verified) / r.total"></i>
          </div>
          <ul class="rc-checks">
            @for (c of r.checks; track c.check) {
              <li [attr.data-state]="c.state" [title]="stateLabel(c.state)"><span class="rc-dot"></span>{{ c.label }}<span class="sr-only">: {{ stateLabel(c.state) }}</span></li>
            }
          </ul>
          @if (r.inResearchSet && r.issues.length) {
            <div class="rc-issues small">Open review issues: {{ issueText() }}</div>
          }
          @if (!r.inResearchSet) {
            <div class="rc-start">
              <span class="muted small">This candidate is not in the research workflow yet.</span>
              <button type="button" class="btn small" [disabled]="starting()" (click)="start()">Start research</button>
            </div>
          }
        </div>
      }

      <label class="rb-field small">Recorded by <span class="muted">(self-reported, not authenticated)</span>
        <input type="text" [ngModel]="recordedBy.name()" (ngModelChange)="recordedBy.set($event)" maxlength="200" placeholder="Your name or initials" />
      </label>

      <div class="feas-list">
        @for (row of rows(); track row.item.itemKey) {
          <app-feasibility-row [siteId]="siteId()" [item]="row.item" [label]="row.label" [core]="row.core" (saved)="reload(); changed.emit()" />
        }
      </div>
    </div>
  `,
})
export class FeasibilityPanelComponent {
  private readonly api = inject(ApiService);
  protected readonly recordedBy = inject(RecordedByStore);

  readonly siteId = input.required<string>();
  /** Emits after a verdict is saved so hosts can refresh summaries. */
  readonly changed = output<void>();

  protected readonly items = signal<FeasibilityItem[]>([]);
  protected readonly summary = signal<FeasibilitySummary | null>(null);
  protected readonly research = signal<SiteResearch | null>(null);
  protected readonly starting = signal(false);

  /** The fixed checklist items (for stable labels/order) joined to the loaded verdicts. */
  protected readonly rows = computed(() => {
    const byKey = new Map(this.items().map(i => [i.itemKey, i]));
    return FEASIBILITY_ITEMS.map(def => ({
      label: def.label,
      core: CORE_CHECK_ITEMS.includes(def.key),
      item: byKey.get(def.key) ?? { itemKey: def.key, status: 'Unknown' as const, evidence: null, reviewer: null, reviewedAtUtc: null, updatedAtUtc: null },
    }));
  });

  protected readonly issueText = computed(() => (this.research()?.issues ?? []).map(i => i.label).join(' · '));
  protected readonly stateLabel = (s: keyof typeof CHECK_STATE_LABELS) => CHECK_STATE_LABELS[s];

  constructor() {
    effect(() => { const id = this.siteId(); if (id) this.load(id); });
  }

  private load(siteId: string) {
    this.api.feasibility(siteId).subscribe({ next: i => this.items.set(i), error: () => {} });
    this.api.feasibilitySummary(siteId).subscribe({ next: s => this.summary.set(s), error: () => {} });
    this.api.researchSite(siteId).subscribe({ next: r => this.research.set(r), error: () => this.research.set(null) });
  }

  protected reload() { this.load(this.siteId()); }

  protected start() {
    this.starting.set(true);
    this.api.startResearch(this.siteId(), this.recordedBy.value()).subscribe({
      next: r => { this.starting.set(false); this.research.set(r); this.changed.emit(); },
      error: () => this.starting.set(false),
    });
  }
}
