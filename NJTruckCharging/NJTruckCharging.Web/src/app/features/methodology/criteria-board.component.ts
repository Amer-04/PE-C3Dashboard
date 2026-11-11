import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { ValidationIssue } from '../../core/methodology.models';
import { ScoringBreakdown, scoringBreakdown } from './criterion-text';

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
  /**
   * Exact plain-language scoring built from the structured rule (see explainScoring). When absent (rules not loaded yet),
   * the details fall back to splitting {@link rule} into rows.
   */
  breakdown?: ScoringBreakdown | null;
}

const CATEGORY_ORDER = ['Access', 'Site', 'Truck demand', 'Charger gap', 'Bonus', 'Penalty'];

/**
 * A single-column list of scoring rules grouped by category, with a text search and category filters. Purely presentational:
 * Edit / enable-disable / remove are emitted to the parent, which decides what they mean (on the overview Edit opens the
 * working draft; in a draft it opens the side drawer). Nothing here changes scoring.
 */
@Component({
  selector: 'app-criteria-board',
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mm-toolbar rl-toolbar">
      <label class="mm-search">
        <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="9" cy="9" r="6" /><path d="m14 14 4 4" /></svg>
        <input type="search" placeholder="Search rules" aria-label="Search rules" [value]="query()" (input)="query.set(val($event))" />
      </label>
      <div class="chips" role="group" aria-label="Show one category">
        <button type="button" class="chip" [class.on]="!category()" [attr.aria-pressed]="!category()" (click)="category.set(null)">
          All<span class="n">{{ items().length }}</span></button>
        @for (g of categoryCounts(); track g.name) {
          <button type="button" class="chip" [class.on]="category() === g.name" [attr.aria-pressed]="category() === g.name"
                  (click)="category.set(category() === g.name ? null : g.name)">{{ g.name }}<span class="n">{{ g.count }}</span></button>
        }
      </div>
    </div>

    @for (g of groups(); track g.name) {
      <section class="rl-group" [attr.aria-label]="g.name + ' rules'">
        <div class="rl-group-head">
          <h3>{{ g.name }}</h3>
          <span>{{ g.items.length }} {{ g.items.length === 1 ? 'rule' : 'rules' }}</span>
        </div>
        <ul class="rl-list">
          @for (c of g.items; track c.key) {
            @let iss = issuesFor(c.key);
            <li class="rl-item" [class.off]="!c.enabled" [class.changed]="isChanged(c.key)" [class.error]="hasError(c.key)"
                [attr.data-key]="c.key">
              <div class="rl-head">
                <h4 class="rl-name">{{ c.name }}</h4>
                <span class="rl-points" [class.neg]="c.max <= 0" [class.mixed]="c.min < 0 && c.max > 0"
                      [attr.aria-label]="'Points: ' + points(c)">{{ points(c) }}</span>
              </div>
              @if (c.explanation) { <p class="rl-desc">{{ c.explanation }}</p> }
              @if (isOpen(c.key)) {
                @let b = details(c);
                <div class="rl-details" [id]="detailsId(c.key)" role="region" [attr.aria-label]="'How points are calculated: ' + c.name">
                  <h5>How points are calculated</h5>
                  @if (b.intro) { <p class="rl-note">{{ b.intro }}</p> }
                  <ul class="rl-bands">
                    @for (r of b.rows; track $index) {
                      <li><span class="rl-when">{{ r.when }}</span>@if (r.points) { <span class="rl-band-pts">{{ r.points }}</span> }</li>
                    }
                  </ul>
                  @for (n of b.notes; track $index) { <p class="rl-note">{{ n }}</p> }
                  @if (b.missing) {
                    <h5>When data is missing</h5>
                    <p class="rl-note">{{ b.missing }}</p>
                  }
                </div>
              }
              @for (e of iss; track $index) {
                <p class="mm-card-issue rl-issue" [class.warn]="e.severity === 'Warning'">{{ e.severity === 'Error' ? '✕' : '⚠' }} {{ e.message }}</p>
              }
              @if ((!c.enabled && !editable()) || !c.builtIn || isChanged(c.key)) {
                <div class="rl-tags">
                  @if (!c.enabled && !editable()) { <span class="pill">Turned off</span> }
                  @if (!c.builtIn) { <span class="pill amber" title="Not part of the original workbook methodology">Added rule</span> }
                  @if (isChanged(c.key)) { <span class="pill amber">Unsaved change</span> }
                </div>
              }
              <div class="rl-actions">
                <button type="button" class="btn rl-btn rl-more" [attr.aria-expanded]="isOpen(c.key)" [attr.aria-controls]="isOpen(c.key) ? detailsId(c.key) : null"
                        [attr.aria-label]="(isOpen(c.key) ? 'Show Less: ' : 'Show More: ') + c.name" (click)="toggleOpen(c.key)">
                  {{ isOpen(c.key) ? 'Show Less' : 'Show More' }}
                  <svg class="rl-icon rl-chev" [class.up]="isOpen(c.key)" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="m5 8 5 5 5-5" /></svg>
                </button>
                @if (canEditRules()) {
                <button type="button" class="btn rl-btn rl-edit" [disabled]="busy()" (click)="edit.emit(c.key)" [attr.aria-label]="'Edit Rule: ' + c.name">
                  <svg class="rl-icon" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M13.5 3.5l3 3L7 16H4v-3z" /></svg>
                  Edit Rule</button>
                }
                @if (editable()) {
                  <label class="mm-switch rl-switch" [title]="c.enabled ? 'Turn this rule off in the draft' : 'Turn this rule on in the draft'">
                    <input type="checkbox" [checked]="c.enabled" (change)="toggle.emit(c.key)" [attr.aria-label]="(c.enabled ? 'Turn off ' : 'Turn on ') + c.name" />
                    <span class="track"></span>{{ c.enabled ? 'On' : 'Off' }}
                  </label>
                  @if (!c.builtIn) {
                    <button type="button" class="btn-ghost rl-btn mm-danger" (click)="remove.emit(c.key)" [attr.aria-label]="'Remove ' + c.name">Remove</button>
                  }
                }
              </div>
            </li>
          }
        </ul>
      </section>
    } @empty {
      <div class="mm-empty"><b>No rules match</b>Try a different search or category.
        <button type="button" class="btn rl-btn" (click)="query.set(''); category.set(null)">Clear filters</button></div>
    }
  `,
})
export class CriteriaBoardComponent {
  readonly items = input.required<CriterionCardItem[]>();
  /** Draft mode: on/off switches and Remove (for added criteria) are shown. */
  readonly editable = input(false);
  /** False when this deployment does not accept methodology changes (hides "Edit Rule"). */
  readonly canEditRules = input(true);
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
    return [...map.entries()].sort((a, b) => rank(a[0]) - rank(b[0])).map(([name, items]) => ({ name, items }));
  });

  /** Rules whose scoring details are shown; each expands independently. */
  protected readonly open = signal<ReadonlySet<string>>(new Set());
  protected isOpen(key: string) { return this.open().has(key); }
  protected toggleOpen(key: string) {
    const next = new Set(this.open());
    if (!next.delete(key)) next.add(key);
    this.open.set(next);
  }
  protected detailsId(key: string) { return 'rl-details-' + key; }
  protected details(c: CriterionCardItem) { return c.breakdown ?? scoringBreakdown(c.rule); }

  protected issuesFor(key: string) { return this.issues()[key] ?? []; }
  protected hasError(key: string) { return this.issuesFor(key).some(i => i.severity === 'Error'); }
  protected isChanged(key: string) { return this.changedKeys().includes(key); }
  /** "+20 pts", "0 to +20 pts", "−15 to +20 pts" — plain words instead of symbols. */
  protected points(c: CriterionCardItem) {
    const f = (v: number) => (v > 0 ? '+' : '') + this.fmt(v);
    return c.min === c.max ? `${f(c.max)} pts` : `${f(c.min)} to ${f(c.max)} pts`;
  }
  protected fmt(v: number) { return (v < 0 ? '−' : '') + Math.abs(v).toLocaleString(undefined, { maximumFractionDigits: 2 }); }
  protected val(e: Event) { return (e.target as HTMLInputElement).value; }
}

function rank(category: string) { const i = CATEGORY_ORDER.indexOf(category); return i < 0 ? 99 : i; }
