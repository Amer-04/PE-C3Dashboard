import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { FEASIBILITY_ITEMS, FeasibilityItem, FeasibilitySummary } from '../../core/models';
import { FeasibilityRowComponent } from './feasibility-row.component';

/**
 * Site feasibility review — a ground/records checklist (Verified / Concern / Unknown + evidence).
 * Kept visually and functionally separate from the C3 score.
 */
@Component({
  selector: 'app-feasibility-panel',
  imports: [FeasibilityRowComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="feasibility">
      <div class="feas-headrow">
        <h3>Site feasibility review</h3>
        @if (summary(); as s) {
          <span class="feas-progress">{{ s.reviewed }}/{{ s.total }} reviewed
            @if (s.concern > 0) { · <span class="neg-text">{{ s.concern }} concern{{ s.concern > 1 ? 's' : '' }}</span> }
          </span>
        }
      </div>
      <p class="hint">A manual verification — <b>independent of the C3 score</b>.</p>
      <div class="feas-list">
        @for (row of rows(); track row.item.itemKey) {
          <app-feasibility-row [siteId]="siteId()" [item]="row.item" [label]="row.label" (saved)="reload(); changed.emit()" />
        }
      </div>
    </div>
  `,
})
export class FeasibilityPanelComponent {
  private readonly api = inject(ApiService);

  readonly siteId = input.required<string>();
  /** Emits after a verdict is saved so hosts can refresh summaries. */
  readonly changed = output<void>();

  protected readonly items = signal<FeasibilityItem[]>([]);
  protected readonly summary = signal<FeasibilitySummary | null>(null);

  /** The fixed checklist items (for stable labels/order) joined to the loaded verdicts. */
  protected readonly rows = computed(() => {
    const byKey = new Map(this.items().map(i => [i.itemKey, i]));
    return FEASIBILITY_ITEMS.map(def => ({
      label: def.label,
      item: byKey.get(def.key) ?? { itemKey: def.key, status: 'Unknown' as const, evidence: null, reviewer: null, reviewedAtUtc: null, updatedAtUtc: null },
    }));
  });

  constructor() {
    effect(() => { const id = this.siteId(); if (id) this.load(id); });
  }

  private load(siteId: string) {
    this.api.feasibility(siteId).subscribe({ next: i => this.items.set(i), error: () => {} });
    this.api.feasibilitySummary(siteId).subscribe({ next: s => this.summary.set(s), error: () => {} });
  }

  protected reload() { this.load(this.siteId()); }
}
