import { ChangeDetectionStrategy, Component, ElementRef, afterNextRender, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/api.service';
import { PreviewSummary, PublishResult } from '../../core/methodology.models';

/**
 * Final confirmation before the live scores of every site are replaced ("Apply Scoring", or "Restore" for a previous
 * method). The typed phrase (APPLY v{id} / RESTORE v{id}, built from the reviewed version) is a deliberate-action
 * safeguard only; the server authorises methodology changes (trusted internal network + same-origin requests) and
 * refuses stale results (rules hash, expected active version and data version must all still match the review).
 */
@Component({
  selector: 'app-publish-dialog',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let p = preview();
    <div class="backdrop" (click)="close()"></div>
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="pub-title" aria-describedby="pub-what pub-kept" (keydown.escape)="close()" (keydown.tab)="trap($event)" #dlg>
      <h3 id="pub-title">{{ restore() ? 'Restore' : 'Apply Scoring' }}: “{{ p.versionName }}” (version {{ p.versionId }})?</h3>
      <p id="pub-what">{{ restore() ? 'Restoring this method' : 'Applying scoring' }} <b>recalculates the score and ranking of all {{ p.sites | number }} candidate sites</b>
        at once: <b>{{ p.scoresChanged | number }}</b> scores and <b>{{ p.ranksChanged | number }}</b> rankings change.
        The dashboard, map, site details, screening, saved sites, comparison and exports all switch to this method.
        The current method (version {{ p.comparedToVersionId }}, “{{ p.comparedToName }}”) is kept and can be restored later.</p>
      <p id="pub-kept" class="muted small">Bookmarks, research, screening decisions, contacts and the original workbook values are not changed.</p>

      <label class="field"><span>Note for the change log (optional)</span>
        <input maxlength="1000" [value]="note()" (input)="note.set(val($event))" />
      </label>
      <label class="field" for="pub-phrase"><span>To confirm, type <code>{{ phrase() }}</code> exactly</span>
        <input #phraseInput id="pub-phrase" autocomplete="off" spellcheck="false" [value]="typed()" (input)="typed.set(val($event))"
               (keydown.enter)="canConfirm() && confirm()" aria-describedby="pub-hint" />
        <span id="pub-hint" class="hint">{{ typed().trim() && !canConfirm() ? 'Not matching yet — type ' + phrase() + '.' : '' }}</span>
      </label>

      @if (error()) { <p class="err" role="alert">{{ error() }}</p> }
      <div class="actions">
        <button type="button" class="btn" (click)="close()" [disabled]="busy()">Cancel</button>
        <button type="button" class="btn primary" [disabled]="!canConfirm()" (click)="confirm()">
          {{ busy() ? (restore() ? 'Restoring…' : 'Applying scoring…') : (restore() ? 'Restore version ' + p.versionId : 'Apply Scoring') }}
        </button>
      </div>
    </div>
  `,
  styles: [`
    .backdrop { position: fixed; inset: 0; background: var(--mm-overlay, rgba(0,0,0,.45)); z-index: 1000; }
    .dialog { position: fixed; z-index: 1001; top: 50%; left: 50%; transform: translate(-50%, -50%); width: min(560px, calc(100vw - 32px));
      max-height: calc(100vh - 32px); overflow: auto; background: var(--surface); color: var(--text); border: 1px solid var(--border);
      border-radius: 14px; padding: 22px 24px; box-shadow: 0 24px 64px rgb(0 0 0 / 35%); }
    .dialog h3 { font-size: 19px; margin: 0 0 10px; line-height: 1.35; }
    .dialog p { color: var(--text); font-size: 15px; margin: 8px 0; line-height: 1.55; }
    .dialog p.small { font-size: 13.5px; color: var(--text-2); }
    .dialog p b { color: var(--text); }
    .field { display: flex; flex-direction: column; gap: 6px; margin: 14px 0; font-size: 14px; font-weight: 600; color: var(--text); }
    .field input, .field textarea { font: inherit; font-size: 15px; font-weight: 400; min-height: 44px; padding: 8px 12px; border: 1px solid var(--border);
      border-radius: 8px; background: var(--surface); color: var(--text); }
    .hint { font-size: 13px; font-weight: 400; color: var(--text-2); min-height: 1em; }
    .actions .btn { min-height: 44px; padding: 0 20px; font-size: 15px; font-weight: 600; }
    .field input:focus, .field textarea:focus { outline: 2px solid var(--accent-soft); border-color: var(--accent); }
    .actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--border); }
    .err { color: var(--critical); }
    :host-context([data-theme="dark"]) .err { color: #f87171; }
    code { font-weight: 700; font-size: 15px; background: var(--pill-amber-bg); color: var(--pill-amber); padding: 1px 6px; border-radius: 4px; }
  `],
})
export class PublishDialogComponent {
  private readonly api = inject(ApiService);

  readonly preview = input.required<PreviewSummary>();
  readonly restore = input(false);
  readonly published = output<PublishResult>();
  readonly cancel = output<void>();

  protected readonly typed = signal('');
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** Built from the reviewed version — e.g. "APPLY v6" — never hard-coded. */
  protected readonly phrase = computed(() => `${this.restore() ? 'RESTORE' : 'APPLY'} v${this.preview().versionId}`);
  protected readonly canConfirm = computed(() =>
    !this.busy() && this.typed().trim().toUpperCase() === this.phrase().toUpperCase());

  private readonly phraseInput = viewChild<ElementRef<HTMLInputElement>>('phraseInput');
  private readonly opener = typeof document !== 'undefined' ? document.activeElement as HTMLElement | null : null;

  constructor() {
    // Move focus into the dialog (the confirmation box) so keyboard users start in the right place.
    afterNextRender(() => this.phraseInput()?.nativeElement.focus());
  }

  private readonly dlg = viewChild<ElementRef<HTMLElement>>('dlg');
  /** Keeps Tab / Shift+Tab inside the dialog while it is open. */
  protected trap(e: Event) {
    const k = e as KeyboardEvent, root = this.dlg()?.nativeElement;
    if (!root) return;
    const items = [...root.querySelectorAll<HTMLElement>('input, button')].filter(x => !(x as HTMLButtonElement).disabled);
    if (!items.length) return;
    const first = items[0], last = items[items.length - 1];
    if (k.shiftKey && document.activeElement === first) { k.preventDefault(); last.focus(); }
    else if (!k.shiftKey && document.activeElement === last) { k.preventDefault(); first.focus(); }
  }

  /** Cancel (button, Escape or backdrop): nothing is sent; focus returns to the button that opened the dialog. */
  protected close() {
    if (this.busy()) return;
    this.cancel.emit();
    this.opener?.focus?.();
  }

  protected confirm() {
    const p = this.preview();
    this.busy.set(true);
    this.error.set(null);
    this.api.publishVersion(p.versionId, p.comparedToVersionId, p.rulesHash, this.note().trim() || null).subscribe({
      next: r => { this.busy.set(false); this.published.emit(r); },
      error: (e: HttpErrorResponse) => { this.busy.set(false); this.error.set(problem(e)); },
    });
  }

  protected val(e: Event) { return (e.target as HTMLInputElement).value; }
}

export function problem(e: HttpErrorResponse): string {
  if (e.status === 0) return 'Cannot reach the API.';
  if (e.status === 403) return (e.error?.title ?? 'Not allowed from this computer.') + (e.error?.detail ? ' ' + e.error.detail : '');
  const issues = (e.error?.issues ?? []) as { message: string }[];
  return [e.error?.title ?? e.message, ...issues.map(i => i.message)].filter(Boolean).join(' · ');
}
