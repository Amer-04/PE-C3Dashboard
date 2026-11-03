import { Injectable, computed, effect, signal } from '@angular/core';
import { COMPARE_MAX, COMPARE_MIN, CompareItem } from './models';

/**
 * Sites picked for side-by-side comparison (2–5), shared by the map/detail panel, the ranked table, Saved
 * Sites and the Compare page. Keyed by the stable SiteId (survives workbook re-imports) and remembered in
 * localStorage as a per-browser convenience. Not a CRM — just a selection.
 */
@Injectable({ providedIn: 'root' })
export class CompareStore {
  static readonly KEY = 'njtc-compare';

  readonly items = signal<CompareItem[]>(CompareStore.load());
  /** Last user-facing notice (e.g. "limit reached"); cleared on the next successful change. */
  readonly notice = signal<string | null>(null);

  readonly count = computed(() => this.items().length);
  readonly siteIds = computed(() => this.items().map(i => i.siteId));
  readonly isFull = computed(() => this.count() >= COMPARE_MAX);
  readonly canCompare = computed(() => this.count() >= COMPARE_MIN);

  constructor() {
    effect(() => {
      const items = this.items();
      try { localStorage.setItem(CompareStore.KEY, JSON.stringify(items)); } catch { /* storage unavailable */ }
    });
  }

  has(siteId: string) { return this.items().some(i => i.siteId === siteId); }

  /** Adds a site; refuses (with a notice) beyond the maximum. Returns whether it is now selected. */
  add(item: CompareItem): boolean {
    if (this.has(item.siteId)) return true;
    if (this.isFull()) {
      this.notice.set(`You can compare up to ${COMPARE_MAX} sites. Remove one first.`);
      return false;
    }
    this.items.update(list => [...list, item]);
    this.notice.set(null);
    return true;
  }

  remove(siteId: string) {
    this.items.update(list => list.filter(i => i.siteId !== siteId));
    this.notice.set(null);
  }

  toggle(item: CompareItem) {
    if (this.has(item.siteId)) this.remove(item.siteId);
    else this.add(item);
  }

  clear() { this.items.set([]); this.notice.set(null); }

  /** Replace the whole selection (e.g. from a shared ?sites= link). Extra ids beyond the maximum are dropped. */
  replace(items: CompareItem[]) {
    const seen = new Set<string>();
    const unique = items.filter(i => i.siteId && !seen.has(i.siteId) && seen.add(i.siteId));
    this.items.set(unique.slice(0, COMPARE_MAX));
    this.notice.set(unique.length > COMPARE_MAX ? `Only the first ${COMPARE_MAX} sites were kept.` : null);
  }

  private static load(): CompareItem[] {
    try {
      const raw = localStorage.getItem(CompareStore.KEY);
      const parsed = raw ? JSON.parse(raw) : [];
      return Array.isArray(parsed)
        ? parsed.filter(i => i && typeof i.siteId === 'string').slice(0, COMPARE_MAX)
        : [];
    } catch {
      return [];
    }
  }
}
