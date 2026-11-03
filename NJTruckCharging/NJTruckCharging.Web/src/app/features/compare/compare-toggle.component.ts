import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { CompareStore } from '../../core/compare-store';
import { CompareItem } from '../../core/models';

/** "+ Compare" / "✓ Comparing" toggle used on the ranked table, the site detail (map) and Saved Sites. */
@Component({
  selector: 'app-compare-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" class="compare-btn" [class.on]="on()" [class.compact]="compact()"
            [disabled]="!on() && store.isFull()"
            [attr.aria-pressed]="on()"
            [title]="on() ? 'Remove from comparison' : store.isFull() ? 'Comparison is full (5 sites) — remove one first' : 'Add to side-by-side comparison'"
            (click)="$event.stopPropagation(); store.toggle(item())">
      {{ on() ? '✓ Comparing' : '+ Compare' }}
    </button>
  `,
})
export class CompareToggleComponent {
  protected readonly store = inject(CompareStore);
  readonly item = input.required<CompareItem>();
  readonly compact = input(false);
  protected readonly on = computed(() => this.store.has(this.item().siteId));
}
