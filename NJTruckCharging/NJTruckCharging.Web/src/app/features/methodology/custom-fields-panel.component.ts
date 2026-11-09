import { ChangeDetectionStrategy, Component, effect, inject, output, signal, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { MethodologyStore } from '../../core/methodology-store';
import { CustomField } from '../../core/methodology.models';
import { CustomImportDialogComponent } from './custom-import-dialog.component';

/** Lists user-defined custom data fields with their coverage; imported fields get "Import values…". */
@Component({
  selector: 'app-custom-fields-panel',
  imports: [DecimalPipe, CustomImportDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mm-section-head">
      <div>
        <h2>Custom data fields</h2>
        <p>Typed fields your team defines with <b>Add New Criterion → Other — Custom Criterion</b>. Sites without a value stay
          <b>Unknown</b> and receive only the criterion's missing-data policy. Values affect scores only after a methodology that uses
          them is previewed and published.</p>
      </div>
      <button type="button" class="btn" (click)="addCustom.emit()">＋ New custom criterion</button>
    </div>
    @if (fields().length) {
      <div class="mm-cards">
        @for (f of fields(); track f.key) {
          <article class="mm-card" [attr.data-key]="f.key">
            <div class="mm-card-top">
              <div class="mm-card-title">{{ f.label }}</div>
              <span class="pill" [class.blue]="f.source === 'Imported'" [class.violet]="f.source === 'Manual'">
                {{ f.source === 'Manual' ? 'Manual entry' : 'Imported dataset' }}</span>
            </div>
            @if (f.description) { <p class="mm-card-text">{{ f.description }}</p> }
            <p class="mm-card-rule">{{ typeText(f) }}</p>
            <div class="cov">
              <span class="mm-cov-bar"><i [style.width.%]="pct(f)"></i></span>
              <span><b>{{ f.valueCount | number }}</b> of {{ f.siteCount | number }} sites have a value · the rest are Unknown</span>
            </div>
            <div class="mm-card-foot">
              @if (f.usedByActive) { <span class="pill green"><span class="dot"></span>Used in active scoring</span> }
              @if (f.usedByVersions.length) { <span class="pill">Used by v{{ f.usedByVersions.join(', v') }}</span> }
              @else { <span class="pill">Not used by any version yet</span> }
              <span class="spacer"></span>
              @if (f.source === 'Imported') {
                <button type="button" class="btn small" (click)="importing.set(f)">Import values…</button>
              } @else { <span class="mm-xs mm-faint">Record values in Site Details</span> }
            </div>
            <span class="mm-xs mm-faint key">Field key: {{ f.key }}</span>
          </article>
        }
      </div>
    } @else {
      <div class="mm-empty">
        <b>No custom data fields yet</b>
        <span>Score something the workbook doesn't have — e.g. “Utility capacity confirmed” or “Zoning outcome”.</span>
        <button type="button" class="btn" (click)="addCustom.emit()">＋ Create a custom criterion</button>
      </div>
    }
    @if (importing(); as f) {
      <app-custom-import-dialog [field]="f" (close)="importing.set(null); load()" (imported)="load()" />
    }
  `,
  styles: [`
    .cov { display: flex; gap: 8px; align-items: center; font-size: 12px; color: var(--text-2); }
    .key { font-family: ui-monospace, monospace; font-size: 11px; }
  `],
})
export class CustomFieldsPanelComponent {
  private readonly api = inject(ApiService);
  private readonly methodology = inject(MethodologyStore);
  protected readonly fields = signal<CustomField[]>([]);
  protected readonly importing = signal<CustomField | null>(null);
  /** "New custom criterion": the parent opens the Add New Criterion wizard. */
  readonly addCustom = output<void>();

  constructor() { effect(() => { this.methodology.active(); untracked(() => this.load()); }); }

  load() { this.api.customFields().subscribe({ next: f => this.fields.set(f), error: () => {} }); }

  protected pct(f: CustomField) { return f.siteCount ? (100 * f.valueCount) / f.siteCount : 0; }
  protected typeText(f: CustomField) {
    return f.type === 'Boolean' ? 'Yes / No' : f.type === 'Category' ? 'Category: ' + f.categories.join(', ')
      : 'Number' + (f.unit ? ` (${f.unit})` : '') + (f.minValue !== null || f.maxValue !== null ? ` · allowed ${f.minValue ?? '−∞'} to ${f.maxValue ?? '∞'}` : '');
  }
}
