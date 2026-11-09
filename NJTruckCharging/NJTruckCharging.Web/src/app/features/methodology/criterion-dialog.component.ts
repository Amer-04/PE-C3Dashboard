import {
  ChangeDetectionStrategy, Component, ElementRef, afterNextRender, computed, effect, inject, input, output, signal, untracked, viewChild,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { CATEGORY_LABELS, Criterion, CriterionCategory, CustomFieldRequest, ScoringFieldInfo, ValidationIssue } from '../../core/methodology.models';
import { CriterionEditorComponent } from './criterion-editor.component';
import { criterionRange, explainCriterion } from './criterion-text';

type Style = 'points' | 'percentile' | 'linear';

/**
 * Side drawer for ONE criterion.
 *  - Edit: name, description, points, thresholds, conditions, bands, on/off and the missing-data policy, with live
 *    validation and a plain-English explanation of the rule.
 *  - Add: a guided wizard — 1. data field (existing, or "Other — Custom Criterion"), 2. how points are calculated,
 *    3. points / conditions / missing data.
 * Changes go into the working draft only — nothing is saved, recalculated or published from here.
 */
@Component({
  selector: 'app-criterion-dialog',
  imports: [DecimalPipe, CriterionEditorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'cancel.emit()' },
  template: `
    <div class="mm-overlay" (click)="cancel.emit()" aria-hidden="true"></div>
    <aside #drawer class="mm-drawer" role="dialog" aria-modal="true" aria-labelledby="crit-dlg-title" tabindex="-1">
      <header class="mm-drawer-head">
        <div class="row">
          <div>
            <h2 id="crit-dlg-title">{{ mode() === 'add' ? 'Add new criterion' : 'Edit criterion' }}</h2>
            <p>{{ mode() === 'add' ? stepIntro() : (criterion()?.name ?? '') + ' · changes go into the draft, not live scoring' }}</p>
          </div>
          <button type="button" class="icon-btn" aria-label="Close" (click)="cancel.emit()">✕</button>
        </div>
        @if (mode() === 'add') {
          <ol class="mm-wizard" aria-label="Steps">
            <li [class.on]="step() === 1" [class.done]="step() > 1" [attr.aria-current]="step() === 1 ? 'step' : null">1 · Data field</li>
            <li [class.on]="step() === 2" [class.done]="step() > 2" [attr.aria-current]="step() === 2 ? 'step' : null">2 · How points are calculated</li>
            <li [class.on]="step() === 3" [attr.aria-current]="step() === 3 ? 'step' : null">3 · Points &amp; conditions</li>
          </ol>
        }
      </header>

      <div class="mm-drawer-body">
        <!-- ===== Add · step 1: data field ===== -->
        @if (mode() === 'add' && step() === 1) {
          <section class="mm-block" aria-labelledby="pick-title">
            <h3 id="pick-title">Which data should this criterion score?</h3>
            <label class="mm-search wide">
              <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="9" cy="9" r="6" /><path d="m14 14 4 4" /></svg>
              <input type="search" placeholder="Search data fields" aria-label="Search data fields" [value]="fieldQuery()" (input)="fieldQuery.set(val($event))" />
            </label>
            <div class="mm-field-list" role="listbox" aria-label="Data fields">
              @if (!fields().length) { <p class="mm-muted mm-xs">Loading data fields…</p> }
              @for (g of groups(); track g.name) {
                <div class="mm-field-group">{{ g.name }}</div>
                @for (f of g.fields; track f.key) {
                  <button type="button" class="mm-field-opt" role="option" [class.on]="f.key === fieldKey()" [attr.aria-selected]="f.key === fieldKey()"
                          [attr.data-field]="f.key" (click)="pickField(f.key)">
                    <span class="nm">{{ f.label }}</span>
                    <span class="ds">{{ typeLabel(f) }} · {{ f.description }}</span>
                    <span class="cov"><span class="mm-cov-bar"><i [style.width.%]="coverage(f)"></i></span>{{ coverage(f) | number: '1.0-0' }}% have data</span>
                  </button>
                }
              } @empty {
                @if (fields().length) { <p class="mm-muted mm-xs">No data fields match “{{ fieldQuery() }}”.</p> }
              }
            </div>
            <button type="button" class="mm-custom-opt" [class.on]="fieldKey() === '__custom__'" (click)="pickField('__custom__')"
                    [attr.aria-pressed]="fieldKey() === '__custom__'">
              <span class="ico" aria-hidden="true">＋</span>
              <span><b>Other — Custom Criterion</b><span class="d">Create a new typed data field (number, yes/no or category) entered per site or imported from a dataset.</span></span>
            </button>
          </section>

          @if (fieldKey() === '__custom__') {
            <section class="mm-block" aria-label="Define a custom data field">
              <h3>New custom data field</h3>
              <p class="sub">The scoring engine only reads the typed values you record per site or import — never formulas or code. Every site starts as <b>Unknown</b>.</p>
              <label class="mm-input"><span>Name</span>
                <input maxlength="80" [value]="cf().label" (input)="setCf({ label: val($event) })" placeholder="e.g. Utility capacity confirmed" aria-label="Custom field name" />
              </label>
              <label class="mm-input"><span>Description</span>
                <textarea rows="2" maxlength="600" [value]="cf().description ?? ''" (input)="setCf({ description: val($event) || null })"
                          placeholder="What the value means and where it comes from" aria-label="Custom field description"></textarea>
              </label>
              <div class="mm-input"><span>Data type</span>
                <div class="mm-seg" role="radiogroup" aria-label="Data type">
                  <label [class.on]="cf().type === 'Number'"><input type="radio" name="cftype" [checked]="cf().type === 'Number'" (change)="setCf({ type: 'Number' })" />Number</label>
                  <label [class.on]="cf().type === 'Boolean'"><input type="radio" name="cftype" [checked]="cf().type === 'Boolean'" (change)="setCf({ type: 'Boolean' })" />Yes / No</label>
                  <label [class.on]="cf().type === 'Category'"><input type="radio" name="cftype" [checked]="cf().type === 'Category'" (change)="setCf({ type: 'Category' })" />Category</label>
                </div>
              </div>
              <div class="mm-input"><span>Where values come from</span>
                <div class="mm-options two">
                  <label class="mm-option" [class.on]="cf().source === 'Manual'">
                    <input type="radio" name="cfsource" class="sr" [checked]="cf().source === 'Manual'" (change)="setCf({ source: 'Manual' })" />
                    <span class="ico" aria-hidden="true">✎</span>
                    <span><b>Entered manually</b><span class="d">Per site in Site Details, with source evidence</span></span>
                  </label>
                  <label class="mm-option" [class.on]="cf().source === 'Imported'">
                    <input type="radio" name="cfsource" class="sr" [checked]="cf().source === 'Imported'" (change)="setCf({ source: 'Imported' })" />
                    <span class="ico" aria-hidden="true">⇪</span>
                    <span><b>Imported from a dataset</b><span class="d">CSV mapped by stable Site ID, with preview</span></span>
                  </label>
                </div>
              </div>
              @if (cf().type === 'Number') {
                <div class="mm-form-grid">
                  <label class="mm-input"><span>Unit <span class="help">optional</span></span><input maxlength="20" [value]="cf().unit ?? ''" (input)="setCf({ unit: val($event) || null })" placeholder="kW, ft, $…" aria-label="Unit" /></label>
                  <label class="mm-input"><span>Minimum allowed <span class="help">optional</span></span><input type="number" step="any" [value]="cf().minValue" (change)="setCf({ minValue: num($event) })" aria-label="Minimum allowed" /></label>
                  <label class="mm-input"><span>Maximum allowed <span class="help">optional</span></span><input type="number" step="any" [value]="cf().maxValue" (change)="setCf({ maxValue: num($event) })" aria-label="Maximum allowed" /></label>
                </div>
              }
              @if (cf().type === 'Category') {
                <label class="mm-input"><span>Allowed values <span class="help">one per line, at least two</span></span>
                  <textarea rows="3" [value]="catsRaw()" (input)="catsRaw.set(val($event))" placeholder="Permitted&#10;Conditional&#10;Prohibited" aria-label="Allowed values"></textarea>
                </label>
              }
              @if (cfError()) { <p class="mm-note bad" role="alert">{{ cfError() }}</p> }
              <p class="mm-xs mm-muted">Creating the field adds no values and changes no score until a criterion that uses it is published.</p>
            </section>
          }

          @if (picked(); as f) {
            <div class="mm-note" [class.warn]="f.missing > 0" [class.good]="f.missing === 0">
              <span><b>{{ f.label }}</b> — {{ f.present | number }} of {{ (f.present + f.missing) | number }} sites have a value
                @if (f.type === 'Number' && f.min !== null) { (range {{ f.min | number: '1.0-2' }} – {{ f.max | number: '1.0-2' }} {{ f.unit }}) }.
                @if (f.missing > 0) { You'll choose what the other {{ f.missing | number }} sites get in step 3. }
                @if (f.isCustom) { {{ f.customSource === 'Imported' ? 'Values are imported under Data Fields → Import values.' : 'Reviewers record values with evidence in Site Details.' }} }</span>
            </div>
          }
        }

        <!-- ===== Add · step 2: method ===== -->
        @if (mode() === 'add' && step() === 2) {
          @if (picked(); as f) {
            <section class="mm-block" aria-labelledby="method-title">
              <h3 id="method-title">How should <i>{{ f.label }}</i> earn points?</h3>
              <div class="mm-options" role="radiogroup" aria-label="Scoring method">
                <button type="button" role="radio" class="mm-option" [class.on]="style() === 'points'" [attr.aria-checked]="style() === 'points'" (click)="setStyle('points')">
                  <span class="ico" aria-hidden="true">±</span>
                  <span><b>Points or penalty when a condition is met</b>
                    <span class="d">e.g. “≥ 3 acres: +20”, “in a flood zone: −10”. Add several bands; the first match wins.</span></span>
                </button>
                <button type="button" role="radio" class="mm-option" [class.on]="style() === 'linear'" [attr.aria-checked]="style() === 'linear'"
                        [disabled]="f.type !== 'Number'" (click)="setStyle('linear')">
                  <span class="ico" aria-hidden="true">⟋</span>
                  <span><b>Linear scale between two values</b>
                    <span class="d">{{ f.type === 'Number' ? 'Full points at one value, falling evenly to 0 at another — e.g. distance to the exit.' : 'Only for number fields.' }}</span></span>
                </button>
                <button type="button" role="radio" class="mm-option" [class.on]="style() === 'percentile'" [attr.aria-checked]="style() === 'percentile'"
                        [disabled]="f.type !== 'Number'" (click)="setStyle('percentile')">
                  <span class="ico" aria-hidden="true">%</span>
                  <span><b>Percentile within the segment</b>
                    <span class="d">{{ f.type === 'Number' ? 'Compares each site with the others in its NOFA segment — the top site gets full points.' : 'Only for number fields.' }}</span></span>
                </button>
              </div>
            </section>
          }
        }

        <!-- ===== Edit, or Add · step 3 ===== -->
        @if (mode() === 'edit' || step() === 3) {
          @if (working(); as c) {
            <section class="mm-explain" aria-live="polite" aria-label="Plain-English explanation">
              <div class="lbl">In plain English</div>
              <p class="txt">{{ explanation() }}</p>
              <div class="meta">
                <span class="pill blue">{{ fmt(range()[0]) }} … {{ fmt(range()[1]) }} pts</span>
                <span class="pill">{{ categoryLabel(c.category) }}</span>
                <span class="pill" [class.green]="c.enabled">{{ c.enabled ? 'Enabled' : 'Disabled' }}</span>
                @if (errorCount()) { <span class="pill red">✕ {{ errorCount() }} to fix</span> }
                @else if (checking()) { <span class="pill">Checking…</span> }
                @else { <span class="pill green">✓ Valid</span> }
              </div>
              @if (issues().length) {
                <ul class="errs" role="alert">
                  @for (e of issues(); track $index) { <li [class.warn]="e.severity === 'Warning'">{{ e.severity === 'Error' ? '✕' : '⚠' }} {{ e.message }}</li> }
                </ul>
              }
            </section>
            <app-criterion-editor [criterion]="c" [fields]="fields()" (changed)="working.set($event)" />
          }
        }
      </div>

      <footer class="mm-drawer-foot">
        <span class="hint">Changes go into the draft. Save the draft, preview all sites, then publish.</span>
        @if (mode() === 'add' && step() > 1) { <button type="button" class="btn" (click)="back()">← Back</button> }
        <button type="button" class="btn-ghost" (click)="cancel.emit()">Cancel</button>
        @if (mode() === 'add' && step() === 1 && fieldKey() === '__custom__') {
          <button type="button" class="btn primary" [disabled]="creating() || !cf().label.trim()" (click)="createField()">
            {{ creating() ? 'Creating…' : 'Create field & continue →' }}</button>
        } @else if (mode() === 'add' && step() < 3) {
          <button type="button" class="btn primary" [disabled]="!picked()" (click)="next()">Next →</button>
        } @else {
          <button type="button" class="btn primary" [disabled]="!canApply()" (click)="apply()">
            {{ mode() === 'add' ? 'Add to draft' : 'Apply to draft' }}</button>
        }
      </footer>
    </aside>
  `,
  styles: [`
    .mm-search.wide { max-width: none; }
    .mm-options.two { grid-template-columns: 1fr 1fr; }
    .mm-option { position: relative; }
    .mm-option .sr { position: absolute; opacity: 0; width: 1px; height: 1px; }
    .mm-option:has(.sr:focus-visible) { outline: 2px solid var(--accent); outline-offset: 2px; }
    @media (max-width: 640px) { .mm-options.two { grid-template-columns: 1fr; } .mm-drawer-foot .hint { display: none; } }
  `],
})
export class CriterionDialogComponent {
  private readonly api = inject(ApiService);
  private readonly drawer = viewChild<ElementRef<HTMLElement>>('drawer');

  readonly mode = input<'edit' | 'add'>('edit');
  readonly criterion = input<Criterion | null>(null);
  readonly fields = input.required<ScoringFieldInfo[]>();
  readonly existingKeys = input<string[]>([]);
  /** Open the wizard with "Other — Custom Criterion" already chosen. */
  readonly startCustom = input(false);
  readonly done = output<Criterion>();
  /** A custom data field was created: the parent reloads the field catalog, then this drawer selects it. */
  readonly fieldsChanged = output<string>();
  readonly cancel = output<void>();

  protected readonly step = signal<1 | 2 | 3>(1);
  protected readonly working = signal<Criterion | null>(null);
  protected readonly fieldKey = signal<string>('');
  protected readonly fieldQuery = signal('');
  protected readonly style = signal<Style>('points');
  protected readonly issues = signal<ValidationIssue[]>([]);
  protected readonly checking = signal(false);
  protected readonly cf = signal<CustomFieldRequest>({ label: '', description: null, type: 'Number', source: 'Manual', unit: null, minValue: null, maxValue: null, categories: null });
  protected readonly cfError = signal<string | null>(null);
  protected readonly creating = signal(false);
  /** Raw textarea text; split into categories only when the field is created (so new lines can be typed). */
  protected readonly catsRaw = signal('');
  private pendingKey: string | null = null;
  private readonly check$ = new Subject<Criterion>();

  protected readonly picked = computed(() => this.fields().find(f => f.key === this.fieldKey()));
  protected readonly groups = computed(() => {
    const q = this.fieldQuery().trim().toLowerCase();
    const map = new Map<string, ScoringFieldInfo[]>();
    for (const f of this.fields())
      if (!q || [f.label, f.description, f.group].some(t => t.toLowerCase().includes(q))) map.set(f.group, [...(map.get(f.group) ?? []), f]);
    return [...map.entries()].map(([name, fields]) => ({ name, fields }));
  });
  protected readonly keyClash = computed(() => {
    const c = this.working();
    return !!c && this.mode() === 'add' && this.existingKeys().includes(c.key);
  });
  protected readonly errorCount = computed(() => this.issues().filter(i => i.severity === 'Error').length);
  protected readonly canApply = computed(() => !!this.working() && !this.keyClash() && !this.checking() && this.errorCount() === 0
    && !!this.working()!.name.trim());
  protected readonly explanation = computed(() => { const c = this.working(); return c ? explainCriterion(c, this.fields()) : ''; });
  protected readonly range = computed(() => { const c = this.working(); return c ? criterionRange(c) : [0, 0]; });
  protected readonly stepIntro = computed(() => ['Choose the data this criterion will score.', 'Choose how the data earns points.',
    'Set the points, conditions and what happens when data is missing.'][this.step() - 1]);

  constructor() {
    effect(() => {
      const c = this.criterion();
      untracked(() => { if (c) this.working.set(structuredClone(c)); });
    });
    effect(() => { if (this.startCustom()) untracked(() => this.pickField('__custom__')); });
    // After a custom field is created the parent reloads the catalog; select the new field and move on to step 2.
    effect(() => {
      const fields = this.fields();
      untracked(() => {
        if (this.pendingKey && fields.some(f => f.key === this.pendingKey)) {
          const k = this.pendingKey; this.pendingKey = null; this.pickField(k); this.step.set(2);
        }
      });
    });
    // Validate just this criterion server-side as it is edited (schema check only — no scoring).
    effect(() => { const c = this.working(); if (c) untracked(() => { this.checking.set(true); this.check$.next(c); }); });
    this.check$.pipe(debounceTime(250), switchMap(c =>
      this.api.validateRules({ schemaVersion: 1, criteria: [{ ...c, name: c.name.trim(), enabled: true }] }).pipe(catchError(() => of(null)))))
      .subscribe(v => {
        const own = (v?.issues ?? []).filter(i => i.path.startsWith('criteria[0]') || i.path === '');
        const clash = this.keyClash() ? [{ severity: 'Error' as const, path: 'criteria[0].key', message: `Key '${this.working()?.key}' is already used in this methodology.` }] : [];
        this.issues.set([...own, ...clash]);
        this.checking.set(false);
      });
    afterNextRender(() => this.drawer()?.nativeElement.focus());
  }

  protected pickField(key: string) {
    if (key !== this.fieldKey()) this.working.set(null);
    this.fieldKey.set(key);
    if (this.picked()?.type !== 'Number' && this.style() !== 'points') this.style.set('points');
  }

  protected setStyle(s: Style) { if (s !== this.style()) { this.style.set(s); this.working.set(null); } }

  protected next() {
    if (this.step() === 1 && this.picked()) this.step.set(2);
    else if (this.step() === 2 && this.picked()) {
      if (!this.working()) this.working.set(this.starter());
      this.step.set(3);
    }
  }
  protected back() { if (this.step() > 1) this.step.set((this.step() - 1) as 1 | 2); }

  /** A sensible first version of a new criterion for the chosen field — every value stays editable. */
  private starter(): Criterion {
    const f = this.picked()!;
    let n = 0, key = keyFor(f.key);
    while (this.existingKeys().includes(key)) key = keyFor(f.key) + (++n + 1);
    const base: Criterion = {
      key, name: f.label, explanation: '', category: f.isCustom ? 'Bonus' : categoryFor(f.group), enabled: true, builtIn: false,
      kind: 'Cases', field: null, fullAt: null, zeroAt: null, maxPoints: null, cases: [], defaultPoints: 0,
      missing: { mode: 'ZeroPoints', points: 0 },
    };
    if (this.style() === 'percentile') return { ...base, kind: 'Percentile', field: f.key, maxPoints: 5, category: f.isCustom ? 'Bonus' : base.category };
    if (this.style() === 'linear') return { ...base, kind: 'Linear', field: f.key, maxPoints: 5, fullAt: f.min ?? 0, zeroAt: f.max ?? 10 };
    const op = f.operators[0];
    const value = f.type === 'Number' ? median(f) : null;
    return { ...base, cases: [{ label: null, points: 5, conditions: [{
      field: f.key, op: f.type === 'Number' ? 'Gte' : op, value, value2: null, values: f.categories ? [f.categories[0]] : [] }] }] };
  }

  protected apply() {
    const c = this.working();
    if (c) this.done.emit({ ...c, name: c.name.trim(), explanation: c.explanation.trim() });
  }

  protected setCf(patch: Partial<CustomFieldRequest>) { this.cf.update(x => ({ ...x, ...patch })); this.cfError.set(null); }

  protected createField() {
    const r = this.cf();
    this.creating.set(true);
    this.cfError.set(null);
    const categories = this.catsRaw().split('\n').map(x => x.trim()).filter(x => x.length > 0);
    this.api.createCustomField({ ...r, label: r.label.trim(), categories: r.type === 'Category' ? categories : null }).subscribe({
      next: f => { this.creating.set(false); this.pendingKey = f.key; this.fieldsChanged.emit(f.key); },
      error: (e: HttpErrorResponse) => { this.creating.set(false); this.cfError.set(e.error?.title ?? 'Could not create the field.'); },
    });
  }

  protected coverage(f: ScoringFieldInfo) { const t = f.present + f.missing; return t ? (100 * f.present) / t : 0; }
  protected typeLabel(f: ScoringFieldInfo) {
    return f.type === 'Number' ? 'Number' + (f.unit ? ` (${f.unit})` : '') : f.type === 'Boolean' ? 'Yes / No' : 'Category';
  }
  protected categoryLabel(c: CriterionCategory) { return CATEGORY_LABELS[c] ?? c; }
  protected fmt(v: number) { return (v > 0 ? '+' : v < 0 ? '−' : '') + Math.abs(v).toLocaleString(undefined, { maximumFractionDigits: 2 }); }
  protected num(e: Event): number | null { const v = (e.target as HTMLInputElement).value; return v === '' || isNaN(+v) ? null : +v; }
  protected val(e: Event) { return (e.target as HTMLSelectElement).value; }
}

function keyFor(field: string) { return (field.replace(/[^a-zA-Z0-9]/g, '') + 'Rule').replace(/^./, m => m.toLowerCase()).slice(0, 36); }
function median(f: ScoringFieldInfo) {
  if (f.min === null || f.max === null) return 0;
  const v = (f.min + f.max) / 2;
  return Math.abs(v) >= 100 ? Math.round(v) : Math.round(v * 100) / 100;
}
function categoryFor(group: string): CriterionCategory {
  switch (group) {
    case 'Access': return 'Access';
    case 'Truck demand': return 'TruckDemand';
    case 'Charger gap': return 'ChargerGap';
    case 'Constraints': return 'Penalty';
    case 'Site': return 'Site';
    default: return 'Bonus';
  }
}
