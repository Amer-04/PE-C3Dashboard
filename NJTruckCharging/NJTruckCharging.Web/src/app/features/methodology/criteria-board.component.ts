import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ValidationIssue } from '../../core/methodology.models';

/** What a criterion card shows — built from the active methodology's views or from a draft's (unsaved) rules. */
export interface CriterionCardItem {
  key: string;
  name: string;
  explanation: string;
  /** Display label of the category ("Truck demand", "Penalty", …). */
  category: string;
  enabled: boolean;
  builtIn: boolean;
  min: number;
  max: number;
  /** Plain-English rule ("≥ 3 ac: +20 · otherwise −15"). */
  rule: string;
  /** Short missing-data policy ("−10 pts") when the criterion reads data that is missing for some sites. */
  missingPolicy: string | null;
}

const CATEGORY_ORDER = ['Access', 'Site', 'Truck demand', 'Charger gap', 'Bonus', 'Penalty'];

/**
 * Compact criterion cards grouped by category, with a text search and category filters. Purely presentational:
 * Edit / enable-disable / remove are emitted to the parent, which decides what they mean (on the overview Edit opens the
 * working draft; in a draft it opens the side drawer). Nothing here changes scoring.
 */
@Component({
  selector: 'app-criteria-board',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mm-toolbar">
      <label class="mm-search">
        <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="9" cy="9" r="6" /><path d="m14 14 4 4" /></svg>
        <input type="search" placeholder="Search criteria" aria-label="Search criteria" [value]="query()" (input)="query.set(val($event))" />
      </label>
      <div class="chips" role="group" aria-label="Filter by category">
        <button type="button" class="chip" [class.on]="!category()" [attr.aria-pressed]="!category()" (click)="category.set(null)">
          All<span class="n">{{ items().length }}</span></button>
        @for (g of categoryCounts(); track g.name) {
          <button type="button" class="chip" [class.on]="category() === g.name" [attr.aria-pressed]="category() === g.name"
                  (click)="category.set(category() === g.name ? null : g.name)">{{ g.name }}<span class="n">{{ g.count }}</span></button>
        }
      </div>
    </div>

    @for (g of groups(); track g.name) {
      <section class="mm-group" [attr.aria-label]="g.name + ' criteria'">
        <div class="mm-group-head">
          <h3>{{ g.name }}</h3>
          <span>{{ g.items.length }} {{ g.items.length === 1 ? 'criterion' : 'criteria' }} · {{ g.enabledMin | number: '1.0-1' }} … {{ g.enabledMax | number: '1.0-1' }} pts when on</span>
        </div>
        <div class="mm-cards">
          @for (c of g.items; track c.key) {
            @let iss = issuesFor(c.key);
            <article class="mm-card" [class.off]="!c.enabled" [class.changed]="isChanged(c.key)" [class.error]="hasError(c.key)"
                     [attr.data-key]="c.key" [attr.aria-label]="c.name">
              <div class="mm-card-top">
                <div class="mm-card-title">{{ c.name }}</div>
                <span class="mm-points" [class.neg]="c.max <= 0" [class.mixed]="c.min < 0 && c.max > 0"
                      [title]="'Lowest and highest points: ' + c.min + ' to ' + c.max">{{ fmt(c.min) }} … {{ fmt(c.max) }} pts</span>
              </div>
              @if (c.explanation) { <p class="mm-card-text" [title]="c.explanation">{{ c.explanation }}</p> }
              <p class="mm-card-rule" [title]="c.rule">{{ c.rule }}</p>
              @for (e of iss; track $index) {
                <p class="mm-card-issue" [class.warn]="e.severity === 'Warning'">{{ e.severity === 'Error' ? '✕' : '⚠' }} {{ e.message }}</p>
              }
              <div class="mm-card-foot">
                @if (editable()) {
                  <label class="mm-switch" [title]="c.enabled ? 'Turn this criterion off in the draft' : 'Turn this criterion on in the draft'">
                    <input type="checkbox" [checked]="c.enabled" (change)="toggle.emit(c.key)" [attr.aria-label]="(c.enabled ? 'Disable ' : 'Enable ') + c.name" />
                    <span class="track"></span>{{ c.enabled ? 'On' : 'Off' }}
                  </label>
                } @else {
                  <span class="pill" [class.green]="c.enabled"><span class="dot"></span>{{ c.enabled ? 'Enabled' : 'Disabled' }}</span>
                }
                @if (!c.builtIn) { <span class="pill amber" title="Not part of the original workbook methodology">Added</span> }
                @if (c.missingPolicy) { <span class="pill" [title]="'Sites without data get ' + c.missingPolicy + ' — values are never estimated'">No data → {{ c.missingPolicy }}</span> }
                @if (isChanged(c.key)) { <span class="pill amber">Unsaved</span> }
                <span class="spacer"></span>
                @if (editable() && !c.builtIn) {
                  <button type="button" class="btn-ghost small mm-danger" (click)="remove.emit(c.key)" [attr.aria-label]="'Remove ' + c.name">Remove</button>
                }
                <button type="button" class="btn small" [disabled]="busy()" (click)="edit.emit(c.key)" [attr.aria-label]="'Edit ' + c.name">Edit</button>
              </div>
            </article>
          }
        </div>
      </section>
    } @empty {
      <div class="mm-empty"><b>No criteria match</b>Try a different search or category.
        <button type="button" class="btn small" (click)="query.set(''); category.set(null)">Clear filters</button></div>
    }
  `,
})
export class CriteriaBoardComponent {
  readonly items = input.required<CriterionCardItem[]>();
  /** Draft mode: on/off switches and Remove (for added criteria) are shown. */
  readonly editable = input(false);
  readonly busy = input(false);
  readonly changedKeys = input<string[]>([]);
  readonly issues = input<Record<string, ValidationIssue[]>>({});
  readonly edit = output<string>();
  readonly toggle = output<string>();
  readonly remove = output<string>();

  protected readonly query = signal('');
  protected readonly category = signal<string | null>(null);

  protected readonly categoryCounts = computed(() => {
    const counts = new Map<string, number>();
    for (const c of this.items()) counts.set(c.category, (counts.get(c.category) ?? 0) + 1);
    return [...counts.entries()].sort((a, b) => rank(a[0]) - rank(b[0])).map(([name, count]) => ({ name, count }));
  });

  protected readonly groups = computed(() => {
    const q = this.query().trim().toLowerCase();
    const cat = this.category();
    const hit = (c: CriterionCardItem) => !q || [c.name, c.explanation, c.rule, c.category].some(t => t.toLowerCase().includes(q));
    const map = new Map<string, CriterionCardItem[]>();
    for (const c of this.items()) if ((!cat || c.category === cat) && hit(c)) map.set(c.category, [...(map.get(c.category) ?? []), c]);
    return [...map.entries()].sort((a, b) => rank(a[0]) - rank(b[0])).map(([name, items]) => {
      const on = items.filter(x => x.enabled);
      return { name, items, enabledMin: on.reduce((t, x) => t + x.min, 0), enabledMax: on.reduce((t, x) => t + x.max, 0) };
    });
  });

  protected issuesFor(key: string) { return this.issues()[key] ?? []; }
  protected hasError(key: string) { return this.issuesFor(key).some(i => i.severity === 'Error'); }
  protected isChanged(key: string) { return this.changedKeys().includes(key); }
  protected fmt(v: number) { return (v < 0 ? '−' : '') + Math.abs(v).toLocaleString(undefined, { maximumFractionDigits: 2 }); }
  protected val(e: Event) { return (e.target as HTMLInputElement).value; }
}

function rank(category: string) { const i = CATEGORY_ORDER.indexOf(category); return i < 0 ? 99 : i; }
