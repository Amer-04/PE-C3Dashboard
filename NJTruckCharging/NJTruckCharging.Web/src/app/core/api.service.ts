import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  CreateContactRequest, DashboardSummary, FeasibilityItem, FeasibilitySummary, FilterOptions, MapsConfig,
  MethodologyNote, PagedResult, PowerEdisonSite, SavedSite, SavedSiteRef, SaveSiteRequest, SiteContact,
  SiteContactSummary, SiteDetail, SiteFilter, SiteListItem, SiteMapPoint, SiteSort, UpdateContactRequest,
  UpdateSavedSiteRequest, UpsertFeasibilityRequest, SiteComparison, SiteScreening, ScreeningSummary,
  UpsertScreeningRequest,
} from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  mapPoints(f: SiteFilter) {
    return this.http.get<SiteMapPoint[]>(`${this.base}/sites/map`, { params: toParams(f) });
  }

  sites(f: SiteFilter, page: number, pageSize: number, sortBy: SiteSort, descending: boolean) {
    const params = toParams(f).set('page', page).set('pageSize', pageSize).set('sortBy', sortBy).set('descending', descending);
    return this.http.get<PagedResult<SiteListItem>>(`${this.base}/sites`, { params });
  }

  site(id: number) { return this.http.get<SiteDetail>(`${this.base}/sites/${id}`); }

  summary(f: SiteFilter) {
    return this.http.get<DashboardSummary>(`${this.base}/dashboard/summary`, { params: toParams(f) });
  }

  exportUrl(f: SiteFilter, format: 'sites' | 'research' = 'sites') {
    const p = format === 'research' ? toParams(f).set('format', 'research') : toParams(f);
    return `${this.base}/sites/export?${p.toString()}`;
  }

  powerEdisonSites() { return this.http.get<PowerEdisonSite[]>(`${this.base}/power-edison-sites`); }
  filterOptions() { return this.http.get<FilterOptions>(`${this.base}/filter-options`); }
  mapsConfig() { return this.http.get<MapsConfig>(`${this.base}/config/maps`); }
  methodology() { return this.http.get<MethodologyNote[]>(`${this.base}/methodology`); }

  // ---- Saved sites (bookmarks) ----
  savedSites() { return this.http.get<SavedSite[]>(`${this.base}/saved-sites`); }
  savedSite(id: number) { return this.http.get<SavedSite>(`${this.base}/saved-sites/${id}`); }
  savedRefs() { return this.http.get<SavedSiteRef[]>(`${this.base}/saved-sites/refs`); }
  saveSite(req: SaveSiteRequest) { return this.http.post<SavedSite>(`${this.base}/saved-sites`, req); }
  updateSaved(id: number, req: UpdateSavedSiteRequest) { return this.http.put<SavedSite>(`${this.base}/saved-sites/${id}`, req); }
  deleteSaved(id: number) { return this.http.delete<void>(`${this.base}/saved-sites/${id}`); }

  // ---- Contacts ----
  contacts(siteId: string) { return this.http.get<SiteContact[]>(`${this.base}/contacts`, { params: { siteId } }); }
  contactSummary(siteId: string) { return this.http.get<SiteContactSummary>(`${this.base}/contacts/summary`, { params: { siteId } }); }
  createContact(req: CreateContactRequest) { return this.http.post<SiteContact>(`${this.base}/contacts`, req); }
  updateContact(id: number, req: UpdateContactRequest) { return this.http.put<SiteContact>(`${this.base}/contacts/${id}`, req); }
  deleteContact(id: number) { return this.http.delete<void>(`${this.base}/contacts/${id}`); }

  // ---- Feasibility ----
  feasibility(siteId: string) { return this.http.get<FeasibilityItem[]>(`${this.base}/feasibility`, { params: { siteId } }); }
  feasibilitySummary(siteId: string) { return this.http.get<FeasibilitySummary>(`${this.base}/feasibility/summary`, { params: { siteId } }); }
  upsertFeasibility(req: UpsertFeasibilityRequest) { return this.http.put<FeasibilityItem>(`${this.base}/feasibility`, req); }

  // ---- Site screening ----
  screening(siteId: string) { return this.http.get<SiteScreening>(`${this.base}/screening`, { params: { siteId } }); }
  screeningSummary(topN: number | null = null) {
    return this.http.get<ScreeningSummary>(`${this.base}/screening/summary`, { params: topN ? { topN } : {} });
  }
  upsertScreening(req: UpsertScreeningRequest) { return this.http.put<SiteScreening>(`${this.base}/screening`, req); }

  // ---- Site comparison ----
  compare(siteIds: string[]) { return this.http.get<SiteComparison>(`${this.base}/compare`, { params: compareParams(siteIds) }); }
  compareExportUrl(siteIds: string[]) { return `${this.base}/compare/export?${compareParams(siteIds).toString()}`; }
}

/** Repeated `siteIds` keys, as ASP.NET Core binds arrays. */
export function compareParams(siteIds: string[]): HttpParams {
  let p = new HttpParams();
  siteIds.forEach(id => (p = p.append('siteIds', id)));
  return p;
}

/** Serialises the filter as query parameters (arrays as repeated keys, as ASP.NET Core binds them). */
export function toParams(f: SiteFilter): HttpParams {
  let p = new HttpParams();
  for (const [key, value] of Object.entries(f)) {
    if (value === null || value === undefined || value === '' || value === false) continue;
    if (Array.isArray(value)) value.forEach(v => (p = p.append(key, String(v))));
    else p = p.set(key, String(value));
  }
  return p;
}
