import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { SavedSite, SavedSiteRef, UpdateSavedSiteRequest } from './models';

/**
 * Bookmark state, shared across the dashboard (save/unsave buttons on the table and detail) and the
 * Saved page. `refs` is keyed by the stable SiteId so any site in the UI can show its bookmarked state
 * in O(1). Mutations update local state only after the API confirms. Not a CRM — just bookmarks.
 */
@Injectable({ providedIn: 'root' })
export class SavedStore {
  private readonly api = inject(ApiService);

  /** SiteId -> bookmark marker, for fast "is this bookmarked" lookups anywhere in the UI. */
  readonly refs = signal<Record<string, SavedSiteRef>>({});
  /** Full bookmarked rows (joined with candidate detail) for the Saved page. */
  readonly saved = signal<SavedSite[]>([]);
  readonly error = signal<string | null>(null);

  /** SiteIds with an in-flight save/unsave, so buttons can disable and duplicate clicks are ignored. */
  private readonly inFlight = signal<Set<string>>(new Set());

  readonly totalSaved = computed(() => Object.keys(this.refs()).length);

  constructor() { this.loadRefs(); }

  isSaved(siteId: string) { return !!this.refs()[siteId]; }
  refFor(siteId: string): SavedSiteRef | undefined { return this.refs()[siteId]; }
  isBusy(siteId: string) { return this.inFlight().has(siteId); }

  loadRefs() {
    this.api.savedRefs().subscribe({
      next: refs => this.refs.set(Object.fromEntries(refs.map(r => [r.siteId, r]))),
      error: () => { /* non-fatal: dashboard still works without bookmark markers */ },
    });
  }

  /** Save/unsave toggle, keyed by SiteId. Guards against duplicate saves and double clicks. */
  toggle(siteId: string) {
    if (this.isBusy(siteId)) return;
    if (this.isSaved(siteId)) this.unsaveBySiteId(siteId);
    else this.save(siteId);
  }

  save(siteId: string) {
    if (this.isSaved(siteId) || this.isBusy(siteId)) return;
    this.setBusy(siteId, true);
    this.error.set(null);
    this.api.saveSite({ siteId }).subscribe({
      next: s => { this.putRef(s); this.setBusy(siteId, false); },
      error: e => { this.error.set(describe(e)); this.setBusy(siteId, false); },
    });
  }

  unsaveBySiteId(siteId: string) {
    const ref = this.refFor(siteId);
    if (ref) this.unsave(ref.id, siteId);
  }

  unsave(id: number, siteId: string) {
    if (this.isBusy(siteId)) return;
    this.setBusy(siteId, true);
    this.error.set(null);
    this.api.deleteSaved(id).subscribe({
      next: () => {
        this.removeRef(siteId);
        this.saved.update(list => list.filter(x => x.id !== id));
        this.setBusy(siteId, false);
      },
      error: e => { this.error.set(describe(e)); this.setBusy(siteId, false); },
    });
  }

  /** Edit a bookmark's note. */
  update(id: number, req: UpdateSavedSiteRequest) {
    this.error.set(null);
    this.api.updateSaved(id, req).subscribe({
      next: updated => {
        this.saved.update(list => list.map(x => (x.id === updated.id ? updated : x)));
        this.putRef(updated);
      },
      error: e => this.error.set(describe(e)),
    });
  }

  /** Load the full bookmark list for the Saved page. */
  loadSaved() {
    this.api.savedSites().subscribe({
      next: s => this.saved.set(s),
      error: e => this.error.set(describe(e)),
    });
  }

  private putRef(s: SavedSite | SavedSiteRef) {
    this.refs.update(m => ({ ...m, [s.siteId]: { id: s.id, siteId: s.siteId } }));
  }
  private removeRef(siteId: string) {
    this.refs.update(m => { const { [siteId]: _drop, ...rest } = m; return rest; });
  }
  private setBusy(siteId: string, busy: boolean) {
    this.inFlight.update(set => { const next = new Set(set); busy ? next.add(siteId) : next.delete(siteId); return next; });
  }
}

function describe(err: unknown): string {
  const e = err as { status?: number; error?: unknown; message?: string };
  if (e?.status === 0) return 'Cannot reach the API. Is the .NET backend running on http://localhost:5172?';
  if (typeof e?.error === 'string' && e.error) return e.error;
  if (e?.status === 404) return 'That candidate is no longer in the dataset.';
  return e?.message ?? 'Could not save changes.';
}
