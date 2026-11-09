import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, debounceTime, of, switchMap, tap } from 'rxjs';
import { ApiService } from './api.service';
import { MethodologyStore } from './methodology-store';
import {
  DEFAULT_FILTER, DashboardSummary, FilterOptions, PagedResult, PowerEdisonSite, SiteDetail,
  SiteFilter, SiteListItem, SiteMapPoint, SiteSort,
} from './models';

/** Dashboard state: one filter drives the map, KPIs, charts and the ranked table. */
@Injectable({ providedIn: 'root' })
export class SiteStore {
  private readonly api = inject(ApiService);
  private readonly methodology = inject(MethodologyStore);

  readonly filter = signal<SiteFilter>({ ...DEFAULT_FILTER });
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly sortBy = signal<SiteSort>('Score');
  readonly descending = signal(true);

  readonly points = signal<SiteMapPoint[]>([]);
  readonly summary = signal<DashboardSummary | null>(null);
  readonly table = signal<PagedResult<SiteListItem> | null>(null);
  readonly powerEdisonSites = signal<PowerEdisonSite[]>([]);
  readonly options = signal<FilterOptions>({ counties: [], utilities: [], highways: [] });

  readonly selectedId = signal<number | null>(null);
  readonly selected = signal<SiteDetail | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly activeFilterCount = computed(() => {
    const f = this.filter();
    return Object.entries(f).filter(([k, v]) =>
      Array.isArray(v) ? v.length > 0 : v !== null && v !== false && v !== DEFAULT_FILTER[k as keyof SiteFilter]).length;
  });

  constructor() {
    // A newly active methodology changes every score and rank: re-run map, KPIs, table and the open detail together.
    effect(() => { if (this.methodology.epoch() > 0) untracked(() => this.refreshSelected()); });

    this.api.powerEdisonSites().subscribe(s => this.powerEdisonSites.set(s));
    this.api.filterOptions().subscribe(o => this.options.set(o));

    const filter$ = toObservable(this.filter).pipe(debounceTime(250));

    // Map points + KPIs follow the filter.
    filter$.pipe(
      tap(() => { this.loading.set(true); this.error.set(null); }),
      switchMap(f => combineLatest([this.api.mapPoints(f), this.api.summary(f)]).pipe(
        catchError(err => { this.error.set(describe(err)); return of(null); }))),
    ).subscribe(result => {
      this.loading.set(false);
      if (!result) return;
      this.points.set(result[0]);
      this.summary.set(result[1]);
    });

    // Ranked table follows filter + paging + sort.
    combineLatest([filter$, toObservable(this.page), toObservable(this.pageSize), toObservable(this.sortBy), toObservable(this.descending)]).pipe(
      debounceTime(50),
      switchMap(([f, page, size, sort, desc]) => this.api.sites(f, page, size, sort, desc).pipe(catchError(() => of(null)))),
    ).subscribe(t => t && this.table.set(t));

    // Site detail follows the selection.
    toObservable(this.selectedId).pipe(
      switchMap(id => id == null ? of(null) : this.api.site(id).pipe(catchError(() => of(null)))),
    ).subscribe(d => this.selected.set(d));
  }

  patch(change: Partial<SiteFilter>) {
    this.filter.update(f => ({ ...f, ...change }));
    this.page.set(1);
  }

  reset() { this.filter.set({ ...DEFAULT_FILTER }); this.page.set(1); }

  toggleSegment(segment: number) {
    const current = this.filter().segments;
    this.patch({ segments: current.includes(segment) ? current.filter(s => s !== segment) : [...current, segment].sort() });
  }

  select(id: number | null) { this.selectedId.set(id); }

  /** Re-reads only the selected site's details (e.g. after a feasibility verdict or contact changes). */
  refreshDetail() {
    const id = this.selectedId();
    if (id != null) this.api.site(id).pipe(catchError(() => of(null))).subscribe(d => d && this.selected.set(d));
  }

  /** Re-reads the selected site and re-runs the list/KPI queries (e.g. after a screening decision changes). */
  refreshSelected() {
    const id = this.selectedId();
    if (id != null) this.api.site(id).pipe(catchError(() => of(null))).subscribe(d => d && this.selected.set(d));
    this.filter.update(f => ({ ...f }));
  }

  sort(by: SiteSort) {
    if (this.sortBy() === by) this.descending.update(d => !d);
    else { this.sortBy.set(by); this.descending.set(by !== 'Rank' && by !== 'MilesToExit' && by !== 'MilesToPowerEdison' ? true : false); }
    this.page.set(1);
  }

  exportUrl() { return this.api.exportUrl(this.filter()); }
  /** Research shortlist CSV (parcel evidence, verified vs preliminary data, professional contact, sources, gaps). */
  researchExportUrl() { return this.api.exportUrl(this.filter(), 'research'); }
}

function describe(err: unknown): string {
  const e = err as { status?: number; message?: string };
  if (e?.status === 0) return 'Cannot reach the API. Is the .NET backend running on http://localhost:5172?';
  return e?.message ?? 'Unexpected error';
}
