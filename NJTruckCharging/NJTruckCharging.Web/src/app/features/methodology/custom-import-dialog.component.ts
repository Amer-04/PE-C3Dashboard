import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/api.service';
import { CustomField, ImportPreview, ImportResult } from '../../core/methodology.models';

/**
 * Import values for an "Imported" custom field from a CSV mapped by stable Site ID: choose file → server validates and
 * previews (unknown Site IDs, duplicates, invalid values, new/updated/unchanged) → commit only when there are no errors.
 * Imported values affect scores only at the next preview/publish of a methodology that uses the field.
 */
@Component({
  selector: 'app-custom-import-dialog',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let f = field();
    <div class="backdrop" (click)="close.emit()"></div>
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="imp-title">
      <header class="dlg-head">
        <h3 id="imp-title">Import values · {{ f.label }}</h3>
        <button type="button" class="icon-btn" aria-label="Close" (click)="close.emit()">✕</button>
      </header>
      <p class="small muted">CSV with a header row: <code>site_id</code>, <code>value</code> (or the field name) and optional <code>evidence</code>.
        Type: <b>{{ f.type === 'Boolean' ? 'yes / no' : f.type === 'Category' ? 'one of ' + f.categories.join(', ') : 'number' + (f.unit ? ' (' + f.unit + ')' : '') }}</b>
        @if (f.minValue !== null || f.maxValue !== null) { · allowed {{ f.minValue ?? '−∞' }} – {{ f.maxValue ?? '∞' }} }.
        Sites not in the file keep their current value (or stay Unknown).</p>

      <label class="file">
        <input type="file" accept=".csv,text/csv" (change)="pick($event)" aria-label="CSV file" />
      </label>

      @if (busy()) { <p class="muted">{{ busy() }}</p> }
      @if (error()) { <p class="err">{{ error() }}</p> }

      @if (preview(); as p) {
        <div class="stats">
          <div><b>{{ p.validRows | number }}</b><span>valid rows</span></div>
          <div><b>{{ p.inserts | number }}</b><span>new values</span></div>
          <div><b>{{ p.updates | number }}</b><span>updated</span></div>
          <div><b>{{ p.unchanged | number }}</b><span>unchanged</span></div>
          <div [class.bad]="p.errors.length"><b>{{ p.errors.length | number }}</b><span>errors</span></div>
          <div><b>{{ p.sitesWithoutValueAfter | number }}</b><span>sites still Unknown</span></div>
        </div>
        @if (p.errors.length) {
          <p class="err small">Fix these rows and choose the file again — nothing is imported while errors remain
            ({{ p.unknownSiteIds }} unknown Site IDs · {{ p.duplicates }} duplicates · {{ p.invalidValues }} invalid values).</p>
          <table class="mini errs">
            <thead><tr><th class="num">Line</th><th>Site ID</th><th>Problem</th></tr></thead>
            <tbody>@for (e of p.errors.slice(0, 50); track $index) { <tr><td class="num">{{ e.line }}</td><td>{{ e.siteId ?? '—' }}</td><td>{{ e.message }}</td></tr> }</tbody>
          </table>
        }
        @if (p.sample.length) {
          <details [open]="!p.errors.length">
            <summary class="small">Sample of rows to import ({{ p.sample.length }})</summary>
            <table class="mini">
              <thead><tr><th class="num">Line</th><th>Site ID</th><th>Value</th><th>Current</th><th>Change</th></tr></thead>
              <tbody>@for (r of p.sample; track r.line) { <tr><td class="num">{{ r.line }}</td><td>{{ r.siteId }}</td><td>{{ r.value }}</td><td>{{ r.current ?? 'Unknown' }}</td><td>{{ r.change }}</td></tr> }</tbody>
            </table>
          </details>
        }
      }
      @if (result(); as r) { <p class="ok">Imported: {{ r.inserted | number }} new, {{ r.updated | number }} updated ({{ r.batch }}). Preview and publish a methodology that uses this field to apply it to scores.</p> }

      <footer class="dlg-foot">
        <button type="button" class="btn-ghost" (click)="close.emit()">{{ result() ? 'Close' : 'Cancel' }}</button>
        <button type="button" class="btn primary" [disabled]="!preview()?.canCommit || !!busy() || !!result()" (click)="commit()">
          Import {{ preview()?.validRows ?? 0 | number }} values
        </button>
      </footer>
    </div>
  `,
  styles: [`
    .backdrop { position: fixed; inset: 0; background: rgba(0,0,0,.45); z-index: 1000; }
    .dialog { position: fixed; z-index: 1001; top: 6vh; left: 50%; transform: translateX(-50%); width: min(780px, calc(100vw - 24px)); max-height: 88vh;
      overflow: auto; background: var(--bg, #f5f7fa); color: var(--text); border-radius: 12px; padding: 16px 18px; box-shadow: 0 20px 60px rgba(0,0,0,.35); border: 1px solid var(--border); }
    .dlg-head { display: flex; justify-content: space-between; align-items: center; }
    .dlg-head h3 { margin: 0; }
    .file { display: block; margin: 10px 0; }
    .stats { display: flex; gap: 10px; flex-wrap: wrap; margin: 8px 0; }
    .stats div { display: flex; flex-direction: column; padding: 6px 10px; border: 1px solid var(--border); border-radius: 8px; min-width: 90px; }
    .stats b { font-size: 18px; } .stats span { font-size: 11px; color: var(--muted); }
    .stats .bad b { color: #d03b3b; }
    .err { color: #d03b3b; } .ok { color: #15803d; font-weight: 600; }
    :host-context([data-theme="dark"]) .err, :host-context([data-theme="dark"]) .stats .bad b { color: #f87171; }
    :host-context([data-theme="dark"]) .ok { color: #4ade80; }
    .small { font-size: 12px; }
    .dlg-foot { display: flex; gap: 8px; justify-content: flex-end; margin-top: 12px; }
  `],
})
export class CustomImportDialogComponent {
  private readonly api = inject(ApiService);
  readonly field = input.required<CustomField>();
  readonly close = output<void>();
  readonly imported = output<ImportResult>();

  protected readonly preview = signal<ImportPreview | null>(null);
  protected readonly result = signal<ImportResult | null>(null);
  protected readonly busy = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  private file: { name: string; text: string } | null = null;

  protected async pick(e: Event) {
    const f = (e.target as HTMLInputElement).files?.[0];
    this.preview.set(null); this.result.set(null); this.error.set(null);
    if (!f) return;
    if (f.size > 5_000_000) { this.error.set('The file is larger than 5 MB.'); return; }
    this.file = { name: f.name, text: await f.text() };
    this.busy.set('Validating every row against the field type and the candidate Site IDs…');
    this.api.previewCustomImport(this.field().key, this.file.name, this.file.text).subscribe({
      next: p => { this.busy.set(null); this.preview.set(p); },
      error: (err: HttpErrorResponse) => { this.busy.set(null); this.error.set(err.error?.title ?? 'Could not read the file.'); },
    });
  }

  protected commit() {
    const p = this.preview();
    if (!p || !this.file) return;
    this.busy.set('Importing…');
    this.api.commitCustomImport(this.field().key, this.file.name, this.file.text, p.token).subscribe({
      next: r => { this.busy.set(null); this.result.set(r); this.imported.emit(r); },
      error: (err: HttpErrorResponse) => { this.busy.set(null); this.error.set(err.error?.title ?? 'Import failed; nothing was changed.'); },
    });
  }
}
