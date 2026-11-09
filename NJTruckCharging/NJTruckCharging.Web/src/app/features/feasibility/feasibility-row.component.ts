import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { FeasibilityItem, FeasibilityStatus, ResearchHistoryEntry } from '../../core/models';
import { RecordedByStore } from '../../core/recorded-by';
import { websiteLink } from '../../shared/external-link';

/** Today as yyyy-MM-dd in local time (the evidence date cannot be later). */
export function todayIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/**
 * What still stops a verdict from being saved as documented evidence, or null when it can be saved.
 * Verified and Concern need evidence text, a source URL and an evidence date; Conflicting must say what disagrees;
 * replacing an earlier verdict needs a correction note.
 */
export function verdictProblem(v: {
  status: FeasibilityStatus; evidence: string; sourceUrl: string; evidenceDate: string; isCorrection: boolean; note: string;
}): string | null {
  if (v.status === 'Verified' || v.status === 'Concern') {
    if (!v.evidence.trim()) return 'Describe the evidence (what was checked and found).';
    if (!/^https?:\/\/\S+\.\S+/i.test(v.sourceUrl.trim())) return 'Add the source URL (http or https).';
    if (!v.evidenceDate) return 'Add the date of the evidence.';
  }
  if (v.status === 'Conflicting' && !v.evidence.trim()) return 'Describe which sources disagree.';
  if (v.evidenceDate && v.evidenceDate > todayIso()) return 'The evidence date cannot be in the future.';
  if (v.isCorrection && !v.note.trim()) return 'Give a reason for changing the earlier verdict.';
  return null;
}

/**
 * One feasibility checklist line: verdict (Unknown / Verified / Concern / Conflicting) with evidence, source URL and
 * evidence date; corrections need a reason; the append-only history is one click away.
 */
@Component({
  selector: 'app-feasibility-row',
  imports: [FormsModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="feas-row" [attr.data-status]="status()" [attr.data-evidence]="item().evidenceState">
      <div class="fr-head">
        <span class="fr-label">{{ label() }} @if (core()) { <span class="core-tag" title="One of the eight core checks behind research completeness">core</span> }</span>
        <select [ngModel]="status()" (ngModelChange)="status.set($event)" [attr.aria-label]="'Verdict: ' + label()">
          <option value="Unknown">Unknown</option>
          <option value="Verified">Verified</option>
          <option value="Concern">Concern</option>
          <option value="Conflicting">Conflicting</option>
        </select>
      </div>
      @if (item().evidenceState === 'InsufficientEvidence' && !dirty()) {
        <div class="ev-flag" role="note">Insufficient evidence — this {{ item().status }} verdict has no {{ missing() }}. It is kept as recorded but not counted as reviewed or verified.</div>
      }
      <textarea rows="1" [ngModel]="evidence()" (ngModelChange)="evidence.set($event)"
                [placeholder]="status() === 'Conflicting' ? 'Which sources disagree, and how…' : 'Evidence — what was checked and found…'"
                [attr.aria-label]="'Evidence: ' + label()"></textarea>
      @if (status() !== 'Unknown') {
        <div class="fr-src">
          <input type="url" [ngModel]="sourceUrl()" (ngModelChange)="sourceUrl.set($event)" placeholder="Source URL (tax map, aerial, utility letter…)" [attr.aria-label]="'Source URL: ' + label()" />
          <input type="date" [ngModel]="evidenceDate()" (ngModelChange)="evidenceDate.set($event)" [max]="today" title="Date of the evidence itself (e.g. imagery or letter date)" [attr.aria-label]="'Evidence date: ' + label()" />
        </div>
      }
      @if (isCorrection()) {
        <input type="text" class="fr-note" [ngModel]="note()" (ngModelChange)="note.set($event)"
               [placeholder]="'Reason for changing the earlier ' + item().status + ' verdict (kept in history)'" [attr.aria-label]="'Correction reason: ' + label()" />
      }
      <div class="fr-foot">
        <span class="muted small">
          @if (item().reviewedAtUtc) {
            Recorded {{ item().reviewedAtUtc | date: 'MMM d' }}@if (item().reviewer) { · {{ item().reviewer }} <i>(self-reported)</i> }
            @if (item().evidenceDate) { · evidence {{ item().evidenceDate }} }
            @if (srcLink(); as l) { · <a [href]="l" target="_blank" rel="noopener noreferrer">source ↗</a> }
          } @else { Not reviewed }
        </span>
        <span class="fr-actions">
          <button type="button" class="btn-ghost small" (click)="toggleHistory()" [attr.aria-expanded]="historyOpen()">History</button>
          <button type="button" class="btn-ghost small" [disabled]="!dirty() || !!problem() || busy()" [title]="problem() ?? ''" (click)="save()">Save</button>
        </span>
      </div>
      @if (dirty() && problem(); as p) { <div class="muted small" role="status">{{ p }}</div> }
      @if (error(); as e) { <div class="neg-text small" role="alert">{{ e }}</div> }
      @if (historyOpen()) {
        <ol class="fr-history" aria-label="History">
          @for (h of history(); track h.id) {
            <li [attr.data-action]="h.action">
              <div><b>{{ h.action === 'Corrected' ? 'Corrected' : 'Recorded' }}</b> {{ h.previousStatus }} → {{ h.status }}
                <span class="muted">· {{ h.recordedAtUtc | date: 'MMM d, y, h:mm a' }}@if (h.recordedBy) { · {{ h.recordedBy }} (self-reported) }</span></div>
              @if (h.evidence) { <div>{{ h.evidence }}</div> }
              <div class="muted">
                @if (h.evidenceDate) { Evidence {{ h.evidenceDate }} }
                @if (h.sourceUrl) { · {{ h.sourceUrl }} }
                @if (!h.sourceUrl && !h.evidenceDate && (h.status === 'Verified' || h.status === 'Concern')) { No source or date recorded }
              </div>
              @if (h.note) { <div class="fr-hnote">Reason: {{ h.note }}</div> }
            </li>
          } @empty { <li class="muted">No history recorded for this check yet.</li> }
        </ol>
      }
    </div>
  `,
})
export class FeasibilityRowComponent {
  private readonly api = inject(ApiService);
  private readonly recordedBy = inject(RecordedByStore);

  readonly siteId = input.required<string>();
  readonly item = input.required<FeasibilityItem>();
  readonly label = input.required<string>();
  /** True for the eight core checks behind research completeness. */
  readonly core = input(false);
  readonly saved = output<void>();

  protected readonly today = todayIso();
  protected readonly status = linkedSignal<FeasibilityStatus>(() => this.item().status);
  protected readonly evidence = linkedSignal(() => this.item().evidence ?? '');
  protected readonly sourceUrl = linkedSignal(() => this.item().sourceUrl ?? '');
  protected readonly evidenceDate = linkedSignal(() => this.item().evidenceDate ?? '');
  protected readonly note = linkedSignal(() => { this.item(); return ''; });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly historyOpen = signal(false);
  protected readonly history = signal<ResearchHistoryEntry[]>([]);

  protected readonly srcLink = computed(() => websiteLink(this.item().sourceUrl)?.href ?? null);
  protected readonly missing = computed(() => {
    const i = this.item();
    const parts = [!i.evidence && 'evidence text', !i.sourceUrl && 'source URL', !i.evidenceDate && 'evidence date'].filter(Boolean);
    return parts.join(', ').replace(/, ([^,]*)$/, ' or $1');
  });

  protected readonly dirty = computed(() =>
    this.status() !== this.item().status || this.evidence() !== (this.item().evidence ?? '') ||
    this.sourceUrl() !== (this.item().sourceUrl ?? '') || this.evidenceDate() !== (this.item().evidenceDate ?? ''));

  /** Replacing an earlier (non-Unknown) verdict with a different one. */
  protected readonly isCorrection = computed(() => this.item().status !== 'Unknown' && this.status() !== this.item().status);

  protected readonly problem = computed(() => verdictProblem({
    status: this.status(), evidence: this.evidence(), sourceUrl: this.sourceUrl(), evidenceDate: this.evidenceDate(),
    isCorrection: this.isCorrection(), note: this.note(),
  }));

  protected toggleHistory() {
    const open = !this.historyOpen();
    this.historyOpen.set(open);
    if (open) this.loadHistory();
  }

  private loadHistory() {
    this.api.researchHistory(this.siteId()).subscribe({
      next: h => this.history.set(h.filter(x => x.itemKey === this.item().itemKey)),
      error: () => this.history.set([]),
    });
  }

  protected save() {
    if (this.busy() || this.problem()) return;
    this.busy.set(true);
    this.error.set(null);
    const keepsSource = this.status() !== 'Unknown';
    this.api.upsertFeasibility({
      siteId: this.siteId(), itemKey: this.item().itemKey, status: this.status(),
      evidence: this.evidence().trim() || null,
      sourceUrl: keepsSource ? this.sourceUrl().trim() || null : null,
      evidenceDate: keepsSource ? this.evidenceDate() || null : null,
      correctionNote: this.note().trim() || null,
      recordedBy: this.recordedBy.value(),
    }).subscribe({
      next: () => {
        this.busy.set(false);
        this.saved.emit();
        if (this.historyOpen()) this.loadHistory();
      },
      error: e => {
        this.busy.set(false);
        this.error.set(typeof e?.error === 'string' && e.error ? e.error : 'Could not save this verdict.');
      },
    });
  }
}
