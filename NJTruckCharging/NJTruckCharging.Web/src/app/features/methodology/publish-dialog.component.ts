import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/api.service';
import { PreviewSummary, PublishResult } from '../../core/methodology.models';

/**
 * Final confirmation before the live scores of every site are replaced. The typed phrase is a deliberate-action
 * safeguard only; the server authorises methodology changes (trusted internal network + same-origin requests).
 */
@Component({
  selector: 'app-publish-dialog',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let p = preview();
    <div class="backdrop" (click)="cancel.emit()"></div>
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="pub-title">
      <h3 id="pub-title">{{ restore() ? 'Restore' : 'Publish' }} methodology v{{ p.versionId }}?</h3>
      <p>This replaces the live score and rank of <b>all {{ p.sites | number }} candidate sites</b> in one atomic step:
        <b>{{ p.scoresChanged | number }}</b> scores and <b>{{ p.ranksChanged | number }}</b> ranks change.
        The dashboard, map, site details, screening queue, saved sites, comparison and CSV exports will all switch to
        “{{ p.versionName }}”. The current version (v{{ p.comparedToVersionId }} “{{ p.comparedToName }}”) stays available for rollback.</p>
      <p class="muted small">Bookmarks, research, screening decisions, contacts and the original workbook values are not touched.</p>

      <label class="field"><span>Note for the history (optional)</span>
        <input maxlength="1000" [value]="note()" (input)="note.set(val($event))" />
      </label>
      <label class="field"><span>Type <code>{{ phrase() }}</code> to confirm</span>
        <input autocomplete="off" [value]="typed()" (input)="typed.set(val($event))" [attr.aria-describedby]="'pub-title'" />
      </label>

      @if (error()) { <p class="err">{{ error() }}</p> }
      <div class="actions">
        <button type="button" class="btn-ghost" (click)="cancel.emit()" [disabled]="busy()">Cancel</button>
        <button type="button" class="btn primary" [disabled]="!canConfirm()" (click)="confirm()">
          {{ busy() ? 'Publishing…' : (restore() ? 'Restore v' : 'Publish v') + p.versionId }}
        </button>
      </div>
    </div>
  `,
  styles: [`
    .backdrop { position: fixed; inset: 0; background: var(--mm-overlay, rgba(0,0,0,.45)); z-index: 1000; }
    .dialog { position: fixed; z-index: 1001; top: 50%; left: 50%; transform: translate(-50%, -50%); width: min(560px, calc(100vw - 32px));
      max-height: calc(100vh - 32px); overflow: auto; background: var(--surface); color: var(--text); border: 1px solid var(--border);
      border-radius: 14px; padding: 22px 24px; box-shadow: 0 24px 64px rgb(0 0 0 / 35%); }
    .dialog h3 { font-size: 17px; margin: 0 0 10px; }
    .dialog p { color: var(--text-2); margin: 8px 0; line-height: 1.5; }
    .dialog p b { color: var(--text); }
    .field { display: flex; flex-direction: column; gap: 5px; margin: 12px 0; font-size: 12.5px; font-weight: 600; color: var(--text-2); }
    .field input, .field textarea { font: inherit; font-size: 14px; font-weight: 400; padding: 8px 10px; border: 1px solid var(--border); border-radius: 8px;
      background: var(--surface); color: var(--text); }
    .field input:focus, .field textarea:focus { outline: 2px solid var(--accent-soft); border-color: var(--accent); }
    .actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--border); }
    .err { color: var(--critical); }
    :host-context([data-theme="dark"]) .err { color: #f87171; }
    .small { font-size: 12px; }
    code { font-weight: 700; background: var(--pill-amber-bg); color: var(--pill-amber); padding: 1px 6px; }
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
  protected readonly phrase = computed(() => `${this.restore() ? 'RESTORE' : 'PUBLISH'} v${this.preview().versionId}`);
  protected readonly canConfirm = computed(() =>
    !this.busy() && this.typed().trim().toUpperCase() === this.phrase().toUpperCase());

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
