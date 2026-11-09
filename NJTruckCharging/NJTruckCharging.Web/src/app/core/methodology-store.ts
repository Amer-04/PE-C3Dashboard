import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, map, of, switchMap, tap } from 'rxjs';
import { ApiService } from './api.service';
import { ActiveMethodology } from './methodology.models';

/**
 * The active scoring methodology, shared by every view. Every API response carries `X-Methodology-Version`; when it
 * differs from the version this tab knows (a publish here or in another tab/browser), `epoch` increments and every
 * score-bearing view reloads — so no screen keeps showing scores from an older version next to newer ones.
 */
@Injectable({ providedIn: 'root' })
export class MethodologyStore {
  private readonly api = inject(ApiService);

  readonly active = signal<ActiveMethodology | null>(null);
  /** Increments whenever the active methodology changes after the first load. Views re-fetch on change. */
  readonly epoch = signal(0);
  readonly versionId = computed(() => this.active()?.version.id ?? null);
  readonly label = computed(() => {
    const a = this.active();
    return a ? `v${a.version.id} · ${a.version.name}` : '';
  });
  readonly maxPossible = computed(() => this.active()?.maxPossible ?? 110);
  /** Max points of the active "Truck demand" criteria (for "x / y" displays). */
  readonly demandMax = computed(() =>
    (this.active()?.criteria ?? []).filter(c => c.enabled && c.category === 'Truck demand').reduce((t, c) => t + c.max, 0));

  private known: number | null = null;

  // Deferred: the first request passes through the interceptor, which injects this store.
  constructor() { queueMicrotask(() => this.refresh()); }

  refresh() {
    this.api.activeMethodology().subscribe({
      next: a => this.apply(a),
      error: () => { /* dashboard still works; the banner just stays empty */ },
    });
  }

  /** Called by the interceptor with the version stamped on an API response. */
  observe(version: number) {
    if (this.known === null || version === this.known) return;
    this.refresh();
  }

  /**
   * The editable working draft based on <paramref name="baseVersionId"/> (default: the active version): reuses the newest
   * unpublished draft built on it, otherwise creates one. Published versions — including the original Version 1 — are
   * never edited in place.
   */
  workingDraft(baseVersionId?: number): Observable<number> {
    const base = baseVersionId ?? this.versionId();
    return this.api.methodologyVersions().pipe(
      switchMap(vs => {
        const existing = vs.find(v => v.status === 'Draft' && v.basedOnVersionId === base);
        return existing ? of(existing.id) : this.api.createDraft(null, base ?? null).pipe(map(d => d.version.id));
      }));
  }

  private apply(a: ActiveMethodology) {
    const changed = this.known !== null && this.known !== a.version.id;
    this.known = a.version.id;
    this.active.set(a);
    if (changed) this.epoch.update(e => e + 1);
  }
}

/** Reads `X-Methodology-Version` from API responses and lets the store detect a publish made elsewhere. */
export const methodologyVersionInterceptor: HttpInterceptorFn = (req, next) => {
  const store = inject(MethodologyStore);
  return next(req).pipe(tap(ev => {
    if (ev instanceof HttpResponse) {
      const v = ev.headers.get('X-Methodology-Version');
      if (v) store.observe(+v);
    }
  }));
};
