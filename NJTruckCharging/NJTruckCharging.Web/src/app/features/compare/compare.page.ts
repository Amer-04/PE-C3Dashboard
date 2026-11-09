import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';

import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService, compareParams } from '../../core/api.service';
import { MethodologyStore } from '../../core/methodology-store';
import { CompareStore } from '../../core/compare-store';
import {
  COMPARE_MAX, COMPARE_MIN, ComparedSite, ComparisonRow, DEFAULT_FILTER, SiteComparison, SiteListItem,
} from '../../core/models';

export interface RowGroup { group: string; rows: ComparisonRow[]; }

/** Rows rendered in the table body (address and the maps link live in the column headers instead). */
const HEADER_ROWS = new Set(['address', 'maps']);

/** Groups rows in API order, optionally keeping only rows whose values differ (non-comparable rows always stay). */
export function groupRows(rows: ComparisonRow[], onlyDifferences: boolean): RowGroup[] {
  const groups: RowGroup[] = [];
  for (const r of rows) {
    if (HEADER_ROWS.has(r.key)) continue;
    if (onlyDifferences && r.comparable && !r.differs) continue;
    const last = groups[groups.length - 1];
    if (last?.group === r.group) last.rows.push(r);
    else groups.push({ group: r.group, rows: [r] });
  }
  return groups;
}

/**
 * Side-by-side comparison of 2–5 C3 candidates. Highlights where sites differ but never declares a winner;
 * unknown / unreviewed values are shown as such (never as verified). Scores are labelled segment-relative.
 */
@Component({
  selector: 'app-compare-page',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel compare-head">
      <div class="panel-head">
        <div>
          <h2>Compare sites</h2>
          <div class="muted small">{{ store.count() }} of {{ max }} selected · pick sites from the map, the ranked table or Saved Sites</div>
        </div>
        <div class="head-actions wrap">
          <label class="toggle-inline">
            <input type="checkbox" [checked]="onlyDiff()" (change)="onlyDiff.set($any($event.target).checked)" />
            Only rows that differ
          </label>
          <a class="btn" [class.disabled]="!store.canCompare()" [attr.aria-disabled]="!store.canCompare()"
             [href]="store.canCompare() ? exportUrl() : null" download>Export CSV</a>
          <button type="button" class="btn" [disabled]="store.count() === 0" (click)="store.clear()">Clear all</button>
          <a class="btn" routerLink="/">← Dashboard</a>
        </div>
      </div>

      <div class="compare-add">
        <input type="search" placeholder="Add a site — search address, municipality or site ID…" aria-label="Search sites to add"
               [value]="query()" (input)="search($any($event.target).value)" [disabled]="store.isFull()" />
        @if (store.isFull()) { <span class="hint">Maximum of {{ max }} sites — remove one to add another.</span> }
        @if (results().length) {
          <ul class="compare-results" role="listbox">
            @for (r of results(); track r.siteId) {
              <li>
                <span><b>{{ r.address }}</b> <small class="muted">{{ r.municipality }} · Seg {{ r.segment }} · #{{ r.rank }} · {{ r.siteId }}</small></span>
                <button type="button" class="btn-ghost small" [disabled]="store.has(r.siteId) || store.isFull()" (click)="addResult(r)">
                  {{ store.has(r.siteId) ? 'Added' : '+ Add' }}
                </button>
              </li>
            }
          </ul>
        }
      </div>

      @if (store.notice(); as n) { <div class="banner warn-banner" role="status">{{ n }}</div> }
    </section>

    @if (!store.canCompare()) {
      <section class="panel empty-state">
        <p><b>Select at least {{ min }} sites to compare.</b></p>
        <p class="muted">Use <b>+ Compare</b> on the ranked table, on a site's detail panel (click it on the map), or on a Saved Sites card — or search above.</p>
        @if (store.items().length) {
          <div class="chips">@for (i of store.items(); track i.siteId) {
            <span class="chip on">{{ i.label }} <button type="button" class="chip-x" (click)="store.remove(i.siteId)" aria-label="Remove">✕</button></span>
          }</div>
        }
      </section>
    } @else {
      @if (error(); as e) { <div class="banner error" role="alert">{{ e }}</div> }
      @if (data(); as d) {
        @if (d.missingSiteIds.length) {
          <div class="banner warn-banner" role="status">
            Not in the current dataset (removed or re-imported): {{ d.missingSiteIds.join(', ') }}.
            <button type="button" class="btn-ghost small" (click)="dropMissing(d.missingSiteIds)">Remove them</button>
          </div>
        }
        <div class="banner score-note" [class.mixed]="d.mixedSegments" role="note">
          @if (d.mixedSegments) { <b>Different segments selected.</b> }
          {{ d.scoreNote }}
        </div>

        <section class="panel compare-panel">
          <div class="compare-legend small">
            <span><i class="tone-dot" data-tone="verified"></i>Verified by reviewer</span>
            <span><i class="tone-dot" data-tone="concern"></i>Reviewer concern</span>
            <span><i class="tone-dot" data-tone="source"></i>Source data (not verified)</span>
            <span><i class="tone-dot" data-tone="unknown"></i>Unknown / not reviewed</span>
            <span><span class="diff-mark">≠</span>Values differ</span>
            <span class="muted">Differences are highlighted only — no site is ranked best.</span>
          </div>

          <div class="table-scroll compare-scroll">
            <table class="compare" [style.--cols]="d.sites.length">
              <thead>
                <tr>
                  <th class="attr-col" scope="col">Attribute</th>
                  @for (s of d.sites; track s.siteId) {
                    <th scope="col" class="site-col">
                      <div class="cs-seg">Segment {{ s.segment }}</div>
                      <div class="cs-addr">{{ s.address }}</div>
                      <div class="muted small">{{ s.municipality }}, {{ s.county }} · {{ s.siteId }}</div>
                      <div class="cs-actions">
                        <a class="btn-ghost small" [href]="s.satelliteUrl" target="_blank" rel="noopener">Satellite ↗</a>
                        <a class="btn-ghost small" [href]="s.streetViewUrl" target="_blank" rel="noopener">Street View ↗</a>
                        <a class="btn-ghost small" [href]="s.googleMapsUrl" target="_blank" rel="noopener">Google Maps ↗</a>
                        <button type="button" class="btn-ghost small danger" (click)="store.remove(s.siteId)" [attr.aria-label]="'Remove ' + s.address">Remove</button>
                      </div>
                    </th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (g of groups(); track g.group) {
                  <tr class="group-row"><th [attr.colspan]="d.sites.length + 1" scope="colgroup">{{ g.group }}</th></tr>
                  @for (r of g.rows; track r.key) {
                    <tr [class.differs]="r.differs" [attr.data-key]="r.key">
                      <th scope="row" class="attr-col">
                        @if (r.differs) { <span class="diff-mark" title="Values differ between sites">≠</span> }
                        {{ r.label }}
                        @if (r.hint) { <div class="hint">{{ r.hint }}</div> }
                      </th>
                      @for (v of r.values; track $index) {
                        <td [attr.data-tone]="r.tones[$index] || null">
                          @switch (r.key) {
                            @case ('missing') {
                              @if (d.sites[$index].missingVerification.length) {
                                <b>{{ d.sites[$index].missingVerification.length }} outstanding</b>
                                <ul class="mini-list">@for (m of d.sites[$index].missingVerification; track m) { <li>{{ m }}</li> }</ul>
                              } @else { All checks reviewed }
                            }
                            @case ('concerns') {
                              @if (d.sites[$index].concerns.length) {
                                <ul class="mini-list">@for (c of d.sites[$index].concerns; track c) { <li>{{ c }}</li> }</ul>
                              } @else { <span class="muted">None recorded</span> }
                            }
                            @default { {{ v }} }
                          }
                        </td>
                      }
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        </section>
      } @else if (loading()) {
        <section class="panel"><p class="muted">Loading comparison…</p></section>
      }
    }
  `,
})
export class ComparePage {
  protected readonly store = inject(CompareStore);
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  protected readonly min = COMPARE_MIN;
  protected readonly max = COMPARE_MAX;
  protected readonly onlyDiff = signal(false);
  protected readonly query = signal('');
  protected readonly results = signal<SiteListItem[]>([]);

  /** Stable key so label-only changes to the selection don't refetch. */
  private readonly ids = computed(() => this.store.siteIds(), { equal: (a, b) => a.join('|') === b.join('|') });

  private readonly methodology = inject(MethodologyStore);

  protected readonly comparison = httpResource<SiteComparison>(() => {
    this.methodology.epoch(); // re-fetch when the active scoring methodology changes
    const ids = this.ids();
    return ids.length >= COMPARE_MIN ? { url: '/api/compare', params: compareParams(ids) } : undefined;
  });

  protected readonly data = computed(() => (this.comparison.hasValue() ? this.comparison.value() : null));
  protected readonly loading = computed(() => this.comparison.isLoading());
  protected readonly error = computed(() => {
    const e = this.comparison.error() as HttpErrorResponse | undefined;
    if (!e) return null;
    if (e.status === 0) return 'Cannot reach the API. Is the .NET backend running on http://localhost:5172?';
    return typeof e.error === 'string' && e.error ? e.error : 'Could not load the comparison.';
  });
  protected readonly groups = computed(() => groupRows(this.data()?.rows ?? [], this.onlyDiff()));
  protected readonly exportUrl = computed(() => this.api.compareExportUrl(this.ids()));

  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    // A shared link (/compare?sites=A,B,C) replaces the current selection.
    const shared = this.route.snapshot.queryParamMap.get('sites');
    if (shared) {
      this.store.replace(shared.split(',').map(s => s.trim()).filter(Boolean)
        .map(siteId => ({ siteId, label: siteId, segment: null })));
    }

    // Fill in friendly labels for ids that arrived without one (e.g. from a shared link).
    effect(() => {
      const d = this.data();
      if (!d) return;
      untracked(() => {
        const bySite = new Map<string, ComparedSite>(d.sites.map(s => [s.siteId, s]));
        const items = this.store.items();
        if (items.some(i => bySite.has(i.siteId) && i.label === i.siteId)) {
          this.store.items.set(items.map(i => {
            const s = bySite.get(i.siteId);
            return s && i.label === i.siteId ? { ...i, label: s.address, segment: s.segment } : i;
          }));
        }
      });
    });
  }

  protected search(term: string) {
    this.query.set(term);
    clearTimeout(this.searchTimer);
    if (term.trim().length < 2) { this.results.set([]); return; }
    this.searchTimer = setTimeout(() => {
      this.api.sites({ ...DEFAULT_FILTER, search: term.trim() }, 1, 8, 'Score', true).subscribe({
        next: page => this.results.set(page.items),
        error: () => this.results.set([]),
      });
    }, 250);
  }

  protected addResult(r: SiteListItem) {
    if (this.store.add({ siteId: r.siteId, label: r.address, segment: r.segment })) {
      this.query.set('');
      this.results.set([]);
    }
  }

  protected dropMissing(ids: string[]) { ids.forEach(id => this.store.remove(id)); }
}
