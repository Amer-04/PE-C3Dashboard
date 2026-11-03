import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { FeasibilityItem, FeasibilityStatus } from '../../core/models';

/** One feasibility checklist line: verdict (Unknown/Verified/Concern) + evidence, with reviewer/date. */
@Component({
  selector: 'app-feasibility-row',
  imports: [FormsModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="feas-row" [attr.data-status]="status()">
      <div class="fr-head">
        <span class="fr-label">{{ label() }}</span>
        <select [ngModel]="status()" (ngModelChange)="status.set($event)" aria-label="Verdict">
          <option value="Unknown">Unknown</option>
          <option value="Verified">Verified</option>
          <option value="Concern">Concern</option>
        </select>
      </div>
      <textarea rows="1" [ngModel]="evidence()" (ngModelChange)="evidence.set($event)" placeholder="Evidence — what was checked and found…"></textarea>
      <div class="fr-foot">
        <span class="muted small">
          @if (item().reviewedAtUtc) { Reviewed {{ item().reviewedAtUtc | date: 'MMM d' }}@if (item().reviewer) { · {{ item().reviewer }} } }
          @else { Not reviewed }
        </span>
        <button type="button" class="btn-ghost small" [disabled]="!dirty() || busy()" (click)="save()">Save</button>
      </div>
      @if (error(); as e) { <div class="neg-text small" role="alert">{{ e }}</div> }
    </div>
  `,
})
export class FeasibilityRowComponent {
  private readonly api = inject(ApiService);

  readonly siteId = input.required<string>();
  readonly item = input.required<FeasibilityItem>();
  readonly label = input.required<string>();
  readonly saved = output<void>();

  protected readonly status = linkedSignal<FeasibilityStatus>(() => this.item().status);
  protected readonly evidence = linkedSignal(() => this.item().evidence ?? '');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly dirty = computed(() =>
    this.status() !== this.item().status || this.evidence() !== (this.item().evidence ?? ''));

  protected save() {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.upsertFeasibility({
      siteId: this.siteId(), itemKey: this.item().itemKey,
      status: this.status(), evidence: this.evidence().trim() || null,
    }).subscribe({
      next: () => { this.busy.set(false); this.saved.emit(); },
      error: e => {
        this.busy.set(false);
        this.error.set(typeof e?.error === 'string' && e.error ? e.error : 'Could not save this verdict.');
      },
    });
  }
}
