import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import {
  CATEGORY_LABELS, ConditionOp, Criterion, CriterionCategory, CriterionKind, OP_LABELS, RuleCondition, ScoreCase,
  ScoringFieldInfo, ValidationIssue,
} from '../../core/methodology.models';

/**
 * Edits one criterion with typed controls only: catalog fields, operators valid for the field type, numeric inputs.
 * Grouped as Basics → How points are calculated → Missing data. Emits a new criterion object on every change (the
 * parent keeps the working rule set immutable); nothing is saved or recalculated here.
 */
@Component({
  selector: 'app-criterion-editor',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let c = criterion();
    <div class="ed">
      <section class="mm-block" aria-labelledby="ed-basics">
        <h3 id="ed-basics">Basics</h3>
        <label class="mm-input"><span>Rule name</span>
          <input [value]="c.name" [readOnly]="readonly()" aria-label="Rule name" maxlength="80" (input)="set({ name: raw($event) })" />
        </label>
        <label class="mm-input"><span>Description <span class="help">— shown in the rules list</span></span>
          <textarea rows="2" [value]="c.explanation" [readOnly]="readonly()" maxlength="600" aria-label="Description"
                    (input)="set({ explanation: raw($event) })"></textarea>
        </label>
        <div class="mm-form-grid">
          <label class="mm-input"><span>Category</span>
            <select [disabled]="readonly()" (change)="set({ category: $any(str($event)) })" aria-label="Category">
              @for (cat of categories; track cat) { <option [value]="cat" [selected]="cat === c.category">{{ categoryLabels[cat] }}</option> }
            </select>
          </label>
          <div class="mm-input"><span>Status</span>
            <label class="mm-switch status">
              <input type="checkbox" [checked]="c.enabled" [disabled]="readonly()" (change)="set({ enabled: chk($event) })" aria-label="Enabled" />
              <span class="track"></span>{{ c.enabled ? 'Enabled — counts toward the score' : 'Disabled — adds no points' }}
            </label>
          </div>
        </div>
        <details class="adv">
          <summary>Advanced</summary>
          <label class="mm-input"><span>Key <span class="help">— stable identifier used in exports{{ c.builtIn ? ' (fixed for original rules)' : '' }}</span></span>
            <input [value]="c.key" [readOnly]="readonly() || c.builtIn" maxlength="40" (change)="set({ key: str($event) })" aria-label="Key" />
          </label>
        </details>
      </section>

      <section class="mm-block" aria-labelledby="ed-points">
        <h3 id="ed-points">How points are calculated</h3>
        <div class="mm-seg" role="radiogroup" aria-label="Scoring method">
          <label [class.on]="c.kind === 'Cases'"><input type="radio" name="kind" [checked]="c.kind === 'Cases'" [disabled]="readonly()" (change)="changeKind('Cases')" />Points by condition</label>
          <label [class.on]="c.kind === 'Linear'"><input type="radio" name="kind" [checked]="c.kind === 'Linear'" [disabled]="readonly() || !numericFields().length" (change)="changeKind('Linear')" />Linear scale</label>
          <label [class.on]="c.kind === 'Percentile'"><input type="radio" name="kind" [checked]="c.kind === 'Percentile'" [disabled]="readonly() || !numericFields().length" (change)="changeKind('Percentile')" />Percentile in segment</label>
        </div>

        @switch (c.kind) {
          @case ('Linear') {
            <div class="mm-form-grid">
              <label class="mm-input wide"><span>Data field</span>
                <select [disabled]="readonly()" (change)="set({ field: str($event) })" aria-label="Field">
                  @for (f of numericFields(); track f.key) { <option [value]="f.key" [selected]="f.key === c.field">{{ f.label }}{{ f.unit ? ' (' + f.unit + ')' : '' }}</option> }
                </select>
              </label>
              <label class="mm-input"><span>Maximum points</span><input type="number" step="any" [value]="c.maxPoints" [readOnly]="readonly()" (change)="set({ maxPoints: num($event) })" aria-label="Max points" /></label>
              <label class="mm-input"><span>Full points at</span><input type="number" step="any" [value]="c.fullAt" [readOnly]="readonly()" (change)="set({ fullAt: num($event) })" aria-label="Full points at" /></label>
              <label class="mm-input"><span>Zero points at</span><input type="number" step="any" [value]="c.zeroAt" [readOnly]="readonly()" (change)="set({ zeroAt: num($event) })" aria-label="Zero points at" /></label>
            </div>
            <p class="hint">Points fall evenly from the maximum at “full points” to 0 at “zero points” (works in either direction).</p>
          }
          @case ('Percentile') {
            <div class="mm-form-grid">
              <label class="mm-input wide"><span>Data field</span>
                <select [disabled]="readonly()" (change)="set({ field: str($event) })" aria-label="Field">
                  @for (f of numericFields(); track f.key) { <option [value]="f.key" [selected]="f.key === c.field">{{ f.label }}{{ f.unit ? ' (' + f.unit + ')' : '' }}</option> }
                </select>
              </label>
              <label class="mm-input"><span>Maximum points</span><input type="number" step="any" [value]="c.maxPoints" [readOnly]="readonly()" (change)="set({ maxPoints: num($event) })" aria-label="Max points" /></label>
            </div>
            <p class="hint">Maximum points × the site's percentile rank within its NOFA segment (ties share the average rank).</p>
          }
          @default {
            <ol class="cases">
              @for (sc of c.cases; track $index; let i = $index) {
                <li class="case">
                  <div class="case-head">
                    <span class="case-no" aria-hidden="true">{{ i + 1 }}</span>
                    <input class="mm-ctl case-label" placeholder="Band label (optional)" [value]="sc.label ?? ''" [readOnly]="readonly()" maxlength="80"
                           (change)="setCase(i, { label: str($event) || null })" [attr.aria-label]="'Band ' + (i + 1) + ' label'" />
                    <label class="pts">Points <input class="mm-ctl" type="number" step="any" [value]="sc.points" [readOnly]="readonly()"
                           (change)="setCase(i, { points: num($event) ?? 0 })" [attr.aria-label]="'Band ' + (i + 1) + ' points'" /></label>
                    @if (!readonly()) {
                      <span class="case-tools">
                        <button type="button" class="icon-btn" title="Move up" aria-label="Move band up" [disabled]="i === 0" (click)="moveCase(i, -1)">↑</button>
                        <button type="button" class="icon-btn" title="Move down" aria-label="Move band down" [disabled]="i === c.cases.length - 1" (click)="moveCase(i, 1)">↓</button>
                        <button type="button" class="icon-btn" title="Remove band" aria-label="Remove band" [disabled]="c.cases.length === 1" (click)="removeCase(i)">✕</button>
                      </span>
                    }
                  </div>
                  @for (cond of sc.conditions; track $index; let j = $index) {
                    @let f = field(cond.field);
                    <div class="cond">
                      <span class="kw">{{ j === 0 ? 'if' : 'and' }}</span>
                      <select class="mm-ctl fsel" [disabled]="readonly()" (change)="changeField(i, j, str($event))" aria-label="Field">
                        @for (g of groups(); track g.name) {
                          <optgroup [label]="g.name">
                            @for (fi of g.fields; track fi.key) { <option [value]="fi.key" [selected]="fi.key === cond.field">{{ fi.label }}</option> }
                          </optgroup>
                        }
                      </select>
                      <select class="mm-ctl osel" [disabled]="readonly()" (change)="setCond(i, j, { op: $any(str($event)) })" aria-label="Operator">
                        @for (op of f?.operators ?? []; track op) { <option [value]="op" [selected]="op === cond.op">{{ opLabels[op] }}</option> }
                      </select>
                      @if (f?.type === 'Number') {
                        <input type="number" step="any" class="mm-ctl v" [value]="cond.value" [readOnly]="readonly()" (change)="setCond(i, j, { value: num($event) })" aria-label="Value" />
                        @if (cond.op === 'Between') {
                          <span class="kw">and</span>
                          <input type="number" step="any" class="mm-ctl v" [value]="cond.value2" [readOnly]="readonly()" (change)="setCond(i, j, { value2: num($event) })" aria-label="Upper value" />
                        }
                        @if (f.unit) { <span class="kw">{{ f.unit }}</span> }
                      }
                      @if (f?.type === 'Category' || f?.type === 'LotCategory') {
                        <span class="cats">
                          @for (v of f?.categories ?? []; track v) {
                            <label class="chip" [class.on]="cond.values.includes(v)">
                              <input type="checkbox" [checked]="cond.values.includes(v)" [disabled]="readonly()" (change)="toggleValue(i, j, v)" />{{ v }}
                            </label>
                          }
                        </span>
                      }
                      @if (!readonly() && sc.conditions.length > 1) {
                        <button type="button" class="icon-btn" title="Remove condition" aria-label="Remove condition" (click)="removeCond(i, j)">✕</button>
                      }
                    </div>
                  }
                  @if (!readonly() && sc.conditions.length < 4) {
                    <button type="button" class="btn-ghost small add-cond" (click)="addCond(i)">＋ and condition</button>
                  }
                </li>
              }
            </ol>
            <div class="case-foot">
              @if (!readonly() && c.cases.length < 12) { <button type="button" class="btn small" (click)="addCase()">＋ Add band</button> }
              <label class="pts">Otherwise <input class="mm-ctl" type="number" step="any" [value]="c.defaultPoints" [readOnly]="readonly()"
                     (change)="set({ defaultPoints: num($event) ?? 0 })" aria-label="Otherwise points" /> pts</label>
            </div>
            <p class="hint">Bands are checked from the top — the first one that matches wins. Use a negative number for a penalty.</p>
          }
        }
      </section>

      @if (missable().length) {
        <section class="mm-block missing" aria-labelledby="ed-missing">
          <h3 id="ed-missing">Missing data</h3>
          <p class="sub">
            @for (f of missable(); track f.key) {
              {{ f.label }}: <b>{{ f.missing | number }}</b> of {{ (f.present + f.missing) | number }} sites have no value{{ $last ? '.' : ';' }}
            }
            No value is ever estimated or filled in.</p>
          <div class="miss-row">
            <label class="mm-input"><span>Sites without data get</span>
              <select [disabled]="readonly()" (change)="setMissing(str($event) === 'FixedPoints' ? 'FixedPoints' : 'ZeroPoints')" aria-label="Missing-data policy">
                <option value="ZeroPoints" [selected]="c.missing.mode === 'ZeroPoints'">0 points</option>
                <option value="FixedPoints" [selected]="c.missing.mode === 'FixedPoints'">a fixed number of points (bonus or penalty)</option>
              </select>
            </label>
            @if (c.missing.mode === 'FixedPoints') {
              <label class="mm-input pts-in"><span>Points</span><input type="number" step="any" [value]="c.missing.points" [readOnly]="readonly()"
                     (change)="set({ missing: { mode: 'FixedPoints', points: num($event) ?? 0 } })" aria-label="Missing-data points" /></label>
            }
          </div>
        </section>
      }

      @if (issues().length) {
        <div class="issues" role="status" aria-live="polite">
          @for (e of issues(); track $index) {
            <p class="mm-note" [class.bad]="e.severity === 'Error'" [class.warn]="e.severity === 'Warning'">{{ e.severity === 'Error' ? '✕' : '⚠' }} {{ e.message }}</p>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .ed { display: grid; gap: 14px; }
    .mm-form-grid .wide { grid-column: 1 / -1; }
    .status { min-height: 34px; }
    .adv summary { cursor: pointer; font-size: 12.5px; color: var(--text-2); font-weight: 600; }
    .adv[open] summary { margin-bottom: 8px; }
    .hint { font-size: 12px; color: var(--text-2); margin: 0; }
    .cases { list-style: none; padding: 0; margin: 0; display: grid; gap: 8px; }
    .case { border: 1px solid var(--border); border-left: 3px solid var(--accent); padding: 8px 10px; border-radius: 8px; background: var(--surface-2); display: grid; gap: 6px; }
    .case-head { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .case-no { font-weight: 700; width: 22px; height: 22px; border-radius: 50%; display: grid; place-items: center; background: var(--accent); color: #fff; font-size: 12px; flex: none; }
    .case-label { flex: 1 1 160px; width: auto; }
    .case-tools { display: inline-flex; gap: 4px; margin-left: auto; }
    .case-tools .icon-btn, .cond .icon-btn { width: 28px; height: 28px; background: var(--surface); border: 1px solid var(--border); color: var(--text-2); }
    .pts { display: inline-flex; gap: 6px; align-items: center; font-size: 12.5px; font-weight: 600; color: var(--text-2); }
    .pts input { width: 80px; }
    .cond { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; padding-left: 30px; font-size: 13px; }
    .cond .kw { color: var(--text-2); font-size: 12.5px; }
    .cond .fsel { width: auto; flex: 1 1 180px; max-width: 280px; }
    .cond .osel { width: auto; flex: none; }
    .cond .v { width: 100px; }
    .add-cond { margin-left: 26px; justify-self: start; }
    .cats { display: inline-flex; gap: 4px; flex-wrap: wrap; }
    .cats .chip { position: relative; display: inline-flex; align-items: center; }
    .cats .chip input { position: absolute; opacity: 0; width: 1px; height: 1px; }
    .cats .chip:has(input:focus-visible) { outline: 2px solid var(--accent); outline-offset: 1px; }
    .case-foot { display: flex; gap: 12px; align-items: center; justify-content: space-between; flex-wrap: wrap; }
    .missing { border-color: color-mix(in srgb, var(--warning) 45%, var(--border)); }
    .miss-row { display: flex; gap: 12px; flex-wrap: wrap; align-items: flex-end; }
    .miss-row .mm-input { flex: 1 1 260px; }
    .miss-row .pts-in { flex: 0 0 120px; }
    .issues { display: grid; gap: 6px; }
    @media (max-width: 640px) { .cond { padding-left: 0; } .add-cond { margin-left: 0; } .case-tools { margin-left: 0; } }
  `],
})
export class CriterionEditorComponent {
  readonly criterion = input.required<Criterion>();
  readonly fields = input.required<ScoringFieldInfo[]>();
  readonly readonly = input(false);
  readonly issues = input<ValidationIssue[]>([]);
  readonly changed = output<Criterion>();

  protected readonly categories: CriterionCategory[] = ['Access', 'Site', 'TruckDemand', 'ChargerGap', 'Penalty', 'Bonus'];
  protected readonly categoryLabels = CATEGORY_LABELS;
  protected readonly opLabels = OP_LABELS;

  protected readonly numericFields = computed(() => this.fields().filter(f => f.type === 'Number'));
  protected readonly groups = computed(() => {
    const map = new Map<string, ScoringFieldInfo[]>();
    for (const f of this.fields()) map.set(f.group, [...(map.get(f.group) ?? []), f]);
    return [...map.entries()].map(([name, fields]) => ({ name, fields }));
  });

  /** Fields this criterion uses that have missing values in the current data. */
  protected readonly missable = computed(() => {
    const c = this.criterion();
    const keys = c.kind === 'Cases' ? c.cases.flatMap(x => x.conditions.map(y => y.field)) : c.field ? [c.field] : [];
    return [...new Set(keys)].map(k => this.field(k)).filter((f): f is ScoringFieldInfo => !!f && (f.missing > 0 || !!f.missingMeaning));
  });

  protected field(key: string | null) { return this.fields().find(f => f.key === key); }

  protected set(patch: Partial<Criterion>) { this.changed.emit({ ...this.criterion(), ...patch }); }
  protected setMissing(mode: 'ZeroPoints' | 'FixedPoints') { this.set({ missing: { mode, points: mode === 'FixedPoints' ? this.criterion().missing.points : 0 } }); }

  protected changeKind(kind: CriterionKind) {
    const c = this.criterion();
    const firstNum = this.numericFields()[0]?.key ?? 'milesToExit';
    if (kind === 'Cases') this.set({ kind, field: null, fullAt: null, zeroAt: null, maxPoints: null,
      cases: c.cases.length ? c.cases : [this.newCase(c.field ?? firstNum)], defaultPoints: 0 });
    else this.set({ kind, field: c.field ?? (c.cases[0]?.conditions[0]?.field && this.numericFields().some(f => f.key === c.cases[0].conditions[0].field)
        ? c.cases[0].conditions[0].field : firstNum), cases: [], defaultPoints: 0, maxPoints: c.maxPoints ?? 5,
      fullAt: kind === 'Linear' ? (c.fullAt ?? 0) : null, zeroAt: kind === 'Linear' ? (c.zeroAt ?? 10) : null });
  }

  private cases(fn: (cases: ScoreCase[]) => ScoreCase[]) { this.set({ cases: fn(this.criterion().cases.map(x => ({ ...x, conditions: [...x.conditions] }))) }); }
  protected setCase(i: number, patch: Partial<ScoreCase>) { this.cases(cs => cs.map((x, k) => k === i ? { ...x, ...patch } : x)); }
  protected addCase() { this.cases(cs => [...cs, this.newCase(cs.at(-1)?.conditions[0]?.field ?? this.numericFields()[0].key)]); }
  protected removeCase(i: number) { this.cases(cs => cs.filter((_, k) => k !== i)); }
  protected moveCase(i: number, d: number) {
    this.cases(cs => { const j = i + d; [cs[i], cs[j]] = [cs[j], cs[i]]; return cs; });
  }

  protected setCond(i: number, j: number, patch: Partial<RuleCondition>) {
    this.cases(cs => cs.map((x, k) => k !== i ? x : { ...x, conditions: x.conditions.map((y, m) => m === j ? { ...y, ...patch } : y) }));
  }
  protected changeField(i: number, j: number, key: string) {
    const f = this.field(key);
    const op: ConditionOp = f?.operators[0] ?? 'Gte';
    this.setCond(i, j, { field: key, op, value: f?.type === 'Number' ? (f.min ?? 0) : null, value2: null,
      values: f?.categories ? [f.categories[0]] : [] });
  }
  protected toggleValue(i: number, j: number, v: string) {
    const cur = this.criterion().cases[i].conditions[j].values;
    this.setCond(i, j, { values: cur.includes(v) ? cur.filter(x => x !== v) : [...cur, v] });
  }
  protected addCond(i: number) {
    const f = this.numericFields()[0];
    this.cases(cs => cs.map((x, k) => k !== i ? x : { ...x, conditions: [...x.conditions, { field: f.key, op: 'Gte', value: 0, value2: null, values: [] }] }));
  }
  protected removeCond(i: number, j: number) {
    this.cases(cs => cs.map((x, k) => k !== i ? x : { ...x, conditions: x.conditions.filter((_, m) => m !== j) }));
  }

  private newCase(field: string): ScoreCase {
    const f = this.field(field);
    return { label: null, points: 0, conditions: [{ field, op: f?.operators[0] ?? 'Gte', value: f?.type === 'Number' ? (f.min ?? 0) : null, value2: null,
      values: f?.categories ? [f.categories[0]] : [] }] };
  }

  protected str(e: Event) { return (e.target as HTMLInputElement).value.trim(); }
  /** Untrimmed text (trimmed when the criterion is applied to the draft) so spaces can be typed. */
  protected raw(e: Event) { return (e.target as HTMLInputElement).value; }
  protected chk(e: Event) { return (e.target as HTMLInputElement).checked; }
  protected num(e: Event): number | null {
    const v = (e.target as HTMLInputElement).value;
    return v === '' || isNaN(+v) ? null : +v;
  }
}
