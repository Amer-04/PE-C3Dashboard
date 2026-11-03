import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { SCREENING_DECISIONS, SCREENING_LABELS, ScreeningDecision, SiteScreening } from '../../core/models';

/**
 * Screening decision for one site: Promising / Needs more research / Unsuitable / Not reviewed, backed by
 * evidence notes, with the assessment history. Suitability only — not a CRM stage; never changes the C3 score.
 */
@Component({
  selector: 'app-screening-panel',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="screening" [attr.data-d]="current()?.decision ?? 'NotReviewed'">
      <div class="scr-head">
        <h3>Screening decision</h3>
        @if (current(); as c) {
          <span class="decision-badge" [attr.data-d]="c.decision">{{ label(c.decision) }}</span>
        }
      </div>
      <p class="hint">Site-suitability judgement from imagery and records — <b>not a sales stage</b> and independent of the C3 score.</p>

      <div class="decision-picker" role="radiogroup" aria-label="Screening decision">
        @for (o of options; track o.value) {
          <button type="button" class="decision-opt" role="radio" [attr.data-d]="o.value"
                  [class.on]="decision() === o.value" [attr.aria-checked]="decision() === o.value"
                  [title]="o.hint" (click)="pick(o.value)">{{ o.label }}</button>
        }
      </div>

      <label class="field">
        <span>Evidence @if (decision() !== 'NotReviewed') { <small class="neg-text">required</small> }</span>
        <textarea rows="3" [value]="evidence()" (input)="evidence.set($any($event.target).value)"
                  placeholder="What did you observe, and where? e.g. 'Satellite 2025: ~3 ac paved yard, two curb cuts on Route 1 frontage.'"></textarea>
      </label>

      <div class="scr-foot">
        <span class="muted small">
          @if (current()?.updatedAtUtc; as u) { Last updated {{ u | date: 'MMM d, y, h:mm a' }} } @else { No decision recorded }
        </span>
        <span class="scr-actions">
          @if (dirty()) { <button type="button" class="btn-ghost small" (click)="revert()">Cancel</button> }
          <button type="button" class="btn primary small" [disabled]="!canSave()" (click)="save()">{{ busy() ? 'Saving…' : 'Save decision' }}</button>
        </span>
      </div>
      @if (validation(); as v) { <div class="neg-text small" role="alert">{{ v }}</div> }
      @if (error(); as e) { <div class="banner error small" role="alert">{{ e }}</div> }

      @if ((current()?.history?.length ?? 0) > 0) {
        <details class="scr-history" [open]="historyOpen()">
          <summary>Assessment history ({{ current()!.history.length }})</summary>
          <ol>
            @for (h of current()!.history; track $index) {
              <li>
                <span class="decision-badge" [attr.data-d]="h.decision">{{ label(h.decision) }}</span>
                <small class="muted">{{ h.recordedAtUtc | date: 'MMM d, y, h:mm a' }}</small>
                @if (h.evidence) { <div class="small">{{ h.evidence }}</div> }
              </li>
            }
          </ol>
        </details>
      }
    </section>
  `,
})
export class ScreeningPanelComponent {
  private readonly api = inject(ApiService);

  readonly siteId = input.required<string>();
  readonly historyOpen = input(false);
  /** Emits the saved state so hosts can update badges / move to the next site. */
  readonly saved = output<SiteScreening>();

  protected readonly options = SCREENING_DECISIONS;
  protected readonly current = signal<SiteScreening | null>(null);
  protected readonly decision = signal<ScreeningDecision>('NotReviewed');
  protected readonly evidence = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly dirty = computed(() => {
    const c = this.current();
    return !!c && (this.decision() !== c.decision || this.evidence().trim() !== (c.evidence ?? ''));
  });
  /** Client-side mirror of the API rule: any decision other than Not reviewed needs evidence. */
  protected readonly validation = computed(() =>
    this.dirty() && this.decision() !== 'NotReviewed' && !this.evidence().trim()
      ? 'Add the evidence behind this decision before saving.' : null);
  protected readonly canSave = computed(() => this.dirty() && !this.validation() && !this.busy());

  constructor() {
    effect(() => this.load(this.siteId()));
  }

  protected label(d: ScreeningDecision) { return SCREENING_LABELS[d] ?? d; }
  protected pick(d: ScreeningDecision) { this.decision.set(d); this.error.set(null); }

  protected revert() {
    const c = this.current();
    if (c) this.apply(c);
  }

  save() {
    if (!this.canSave()) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.upsertScreening({ siteId: this.siteId(), decision: this.decision(), evidence: this.evidence().trim() || null })
      .subscribe({
        next: s => { this.busy.set(false); this.apply(s); this.saved.emit(s); },
        error: e => {
          this.busy.set(false);
          this.error.set(typeof e?.error === 'string' && e.error ? e.error
            : e?.status === 0 ? 'Cannot reach the API.' : 'Could not save the decision.');
        },
      });
  }

  private load(siteId: string) {
    this.current.set(null);
    this.error.set(null);
    this.api.screening(siteId).subscribe({
      next: s => { if (s.siteId === this.siteId()) this.apply(s); },
      error: () => this.error.set('Could not load the screening decision.'),
    });
  }

  private apply(s: SiteScreening) {
    this.current.set(s);
    this.decision.set(s.decision);
    this.evidence.set(s.evidence ?? '');
  }
}
