import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/api.service';
import { CustomValue } from '../../core/methodology.models';

/**
 * Site Details: the site's value for every custom data field. Manual fields can be recorded with source evidence
 * (typed input only); imported fields are read-only. No value = Unknown — nothing is ever filled in.
 */
@Component({
  selector: 'app-custom-values-panel',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (values().length) {
      <section class="cv">
        <h4>Custom criteria data</h4>
        <p class="hint">Recorded values affect the C3 score only after the methodology is next previewed and published (or re-scored).</p>
        @for (v of values(); track v.fieldKey) {
          <div class="cv-row">
            <div class="cv-head">
              <b>{{ v.label }}</b>
              @if (v.usedByActive) { <span class="tag">in active scoring</span> }
              <span class="status" [class.unknown]="v.status === 'Unknown'">{{ v.status === 'Unknown' ? 'Unknown' : v.display }}</span>
            </div>
            @if (v.status === 'Recorded') {
              <div class="small muted">{{ v.source === 'Imported' ? 'Imported' : 'Evidence' }}: {{ v.evidence || '—' }}
                @if (v.importBatch) { · {{ v.importBatch }} } · {{ v.updatedAtUtc | date: 'mediumDate' }}</div>
            }
            @if (v.source === 'Manual') {
              @if (editing() === v.fieldKey) {
                <div class="cv-form">
                  @switch (v.type) {
                    @case ('Number') {
                      <input type="number" step="any" [attr.min]="v.minValue" [attr.max]="v.maxValue" [value]="draftValue()"
                             (input)="setDraft(val($event))" [attr.aria-label]="v.label" />
                      @if (v.unit) { <span class="muted small">{{ v.unit }}</span> }
                    }
                    @case ('Boolean') {
                      <select (change)="setDraft(val($event))" [attr.aria-label]="v.label">
                        <option value="" [selected]="!draftValue()">Choose…</option>
                        <option value="yes" [selected]="draftValue() === 'yes'">Yes</option>
                        <option value="no" [selected]="draftValue() === 'no'">No</option>
                      </select>
                    }
                    @default {
                      <select (change)="setDraft(val($event))" [attr.aria-label]="v.label">
                        <option value="" [selected]="!draftValue()">Choose…</option>
                        @for (c of v.categories; track c) { <option [value]="c" [selected]="draftValue() === c">{{ c }}</option> }
                      </select>
                    }
                  }
                  <textarea rows="2" maxlength="2000" placeholder="Source evidence (document, call, site visit, link) — required"
                            [value]="draftEvidence()" (input)="draftEvidence.set(val($event)); error.set(null)" aria-label="Source evidence"></textarea>
                  @if (error()) { <p class="err small">{{ error() }}</p> }
                  <div class="btns">
                    <button type="button" class="btn small primary" [disabled]="busy()" (click)="save(v)">Save value</button>
                    <button type="button" class="btn-ghost small" (click)="editing.set(null)">Cancel</button>
                  </div>
                </div>
              } @else {
                <div class="btns">
                  <button type="button" class="btn small" (click)="edit(v)">{{ v.status === 'Unknown' ? 'Record value' : 'Edit' }}</button>
                  @if (v.status === 'Recorded') { <button type="button" class="btn-ghost small" [disabled]="busy()" (click)="clear(v)">Clear (back to Unknown)</button> }
                </div>
              }
            } @else if (v.status === 'Unknown') {
              <div class="small muted">Not in any imported dataset yet.</div>
            }
          </div>
        }
      </section>
    }
  `,
  styles: [`
    .cv { margin-top: 12px; }
    .cv h4 { margin: 0 0 4px; }
    .hint { font-size: 11px; color: var(--muted); margin: 0 0 6px; }
    .cv-row { border: 1px solid var(--border); border-radius: 8px; padding: 6px 8px; margin-bottom: 6px; }
    .cv-head { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; font-size: 13px; }
    .status { margin-left: auto; font-weight: 600; }
    .status.unknown { color: var(--muted); font-weight: 500; }
    .tag { font-size: 10px; padding: 1px 6px; border-radius: 8px; background: #e8eef8; color: #1e3a8a; }
    :host-context([data-theme="dark"]) .tag { background: #1e2a44; color: #bcd0ff; }
    .cv-form { display: flex; flex-direction: column; gap: 6px; margin-top: 6px; }
    .cv-form input, .cv-form select, .cv-form textarea { font: inherit; font-size: 13px; padding: 4px 6px; }
    .btns { display: flex; gap: 6px; margin-top: 4px; }
    .small { font-size: 12px; }
    .err { color: #d03b3b; margin: 0; }
    :host-context([data-theme="dark"]) .err { color: #f87171; }
  `],
})
export class CustomValuesPanelComponent {
  private readonly api = inject(ApiService);
  readonly siteId = input.required<string>();
  readonly changed = output<void>();

  protected readonly values = signal<CustomValue[]>([]);
  protected readonly editing = signal<string | null>(null);
  protected readonly draftValue = signal('');
  protected readonly draftEvidence = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() { effect(() => { const id = this.siteId(); untracked(() => { this.editing.set(null); this.load(id); }); }); }

  private load(id: string) { this.api.customValues(id).subscribe({ next: v => this.values.set(v), error: () => this.values.set([]) }); }

  protected edit(v: CustomValue) {
    this.error.set(null);
    this.draftValue.set(v.type === 'Number' ? (v.numberValue?.toString() ?? '') : v.type === 'Boolean' ? (v.boolValue == null ? '' : v.boolValue ? 'yes' : 'no') : (v.categoryValue ?? ''));
    this.draftEvidence.set(v.evidence ?? '');
    this.editing.set(v.fieldKey);
  }

  protected save(v: CustomValue) {
    this.busy.set(true);
    this.api.setCustomValue(v.fieldKey, this.siteId(), this.draftValue(), this.draftEvidence().trim() || null).subscribe({
      next: () => { this.busy.set(false); this.editing.set(null); this.load(this.siteId()); this.changed.emit(); },
      error: (e: HttpErrorResponse) => { this.busy.set(false); this.error.set(e.error?.title ?? 'Could not save the value.'); },
    });
  }

  protected clear(v: CustomValue) {
    this.busy.set(true);
    this.api.clearCustomValue(v.fieldKey, this.siteId()).subscribe({
      next: () => { this.busy.set(false); this.load(this.siteId()); this.changed.emit(); },
      error: () => this.busy.set(false),
    });
  }

  protected setDraft(v: string) { this.draftValue.set(v); this.error.set(null); }

  protected val(e: Event) { return (e.target as HTMLInputElement).value; }
}
