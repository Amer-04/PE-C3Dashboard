import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  CreateContactRequest, DashboardSummary, FeasibilityItem, FeasibilitySummary, FilterOptions, MapsConfig,
  MethodologyNote, PagedResult, PowerEdisonSite, SavedSite, SavedSiteRef, SaveSiteRequest, SiteContact,
  SiteContactSummary, SiteDetail, SiteFilter, SiteListItem, SiteMapPoint, SiteSort, UpdateContactRequest,
  UpdateSavedSiteRequest, UpsertFeasibilityRequest, SiteComparison, SiteScreening, ScreeningSummary,
  UpsertScreeningRequest, ResearchHistoryEntry, SiteResearch, ReviewQueue, ResearchSummary,
} from './models';
import {
  Activation, ActiveMethodology, ComparisonPage, MethodologyRules, PreviewSummary, PublishResult, ScoringFieldInfo,
  ValidationResult, VersionDetail, VersionSummary, CustomField, CustomFieldRequest, CustomValue, ImportPreview, ImportResult,
} from './methodology.models';

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

  // ---- Editable methodology (writes are authorised server-side by trusted network + same origin; no credential in the browser) ----
  activeMethodology() { return this.http.get<ActiveMethodology>(`${this.base}/methodology/active`); }
  methodologyFields() { return this.http.get<ScoringFieldInfo[]>(`${this.base}/methodology/fields`); }
  methodologyVersions() { return this.http.get<VersionSummary[]>(`${this.base}/methodology/versions`); }
  methodologyVersion(id: number) { return this.http.get<VersionDetail>(`${this.base}/methodology/versions/${id}`); }
  methodologyActivations() { return this.http.get<Activation[]>(`${this.base}/methodology/activations`); }
  validateRules(rules: MethodologyRules) { return this.http.post<ValidationResult>(`${this.base}/methodology/validate`, rules); }
  methodologyComparison(id: number, q: { segment?: number | null; sort?: string; search?: string; page: number; pageSize: number }) {
    let p = new HttpParams().set('page', q.page).set('pageSize', q.pageSize);
    if (q.segment) p = p.set('segment', q.segment);
    if (q.sort) p = p.set('sort', q.sort);
    if (q.search) p = p.set('search', q.search);
    return this.http.get<ComparisonPage>(`${this.base}/methodology/versions/${id}/comparison`, { params: p });
  }
  createDraft(name: string | null, basedOnVersionId: number | null) {
    return this.http.post<VersionDetail>(`${this.base}/methodology/drafts`, { name, basedOnVersionId });
  }
  duplicateVersion(id: number, name: string | null) {
    return this.http.post<VersionDetail>(`${this.base}/methodology/versions/${id}/duplicate`, { name });
  }
  saveDraft(id: number, name: string, changeSummary: string | null, rules: MethodologyRules) {
    return this.http.put<VersionDetail>(`${this.base}/methodology/drafts/${id}`, { name, changeSummary, rules });
  }
  deleteDraft(id: number) { return this.http.delete<void>(`${this.base}/methodology/drafts/${id}`); }
  previewVersion(id: number, topN: number) {
    return this.http.post<PreviewSummary>(`${this.base}/methodology/versions/${id}/preview?topN=${topN}`, null);
  }
  // ---- Custom data fields (typed; values recorded per site with evidence, or imported by Site ID) ----
  customFields() { return this.http.get<CustomField[]>(`${this.base}/custom-fields`); }
  createCustomField(req: CustomFieldRequest) { return this.http.post<CustomField>(`${this.base}/custom-fields`, req); }
  updateCustomField(key: string, req: CustomFieldRequest) { return this.http.put<CustomField>(`${this.base}/custom-fields/${key}`, req); }
  customValues(siteId: string) { return this.http.get<CustomValue[]>(`${this.base}/custom-fields/sites/${encodeURIComponent(siteId)}`); }
  setCustomValue(key: string, siteId: string, value: string, evidence: string | null) {
    return this.http.put<CustomValue>(`${this.base}/custom-fields/${key}/sites/${encodeURIComponent(siteId)}`, { value, evidence });
  }
  clearCustomValue(key: string, siteId: string) {
    return this.http.delete<void>(`${this.base}/custom-fields/${key}/sites/${encodeURIComponent(siteId)}`);
  }
  previewCustomImport(key: string, fileName: string, csv: string) {
    return this.http.post<ImportPreview>(`${this.base}/custom-fields/${key}/import/preview`, { fileName, csv });
  }
  commitCustomImport(key: string, fileName: string, csv: string, token: string) {
    return this.http.post<ImportResult>(`${this.base}/custom-fields/${key}/import/commit`, { fileName, csv, token });
  }

  publishVersion(id: number, expectedActiveVersionId: number, rulesHash: string, note: string | null) {
    return this.http.post<PublishResult>(`${this.base}/methodology/versions/${id}/publish`,
      { confirm: true, expectedActiveVersionId, rulesHash, note });
  }

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

  // ---- Research quality (Phase 1; independent of the C3 score) ----
  researchHistory(siteId: string) { return this.http.get<ResearchHistoryEntry[]>(`${this.base}/research/history`, { params: { siteId } }); }
  researchSite(siteId: string) { return this.http.get<SiteResearch>(`${this.base}/research/site`, { params: { siteId } }); }
  startResearch(siteId: string, recordedBy: string | null, note: string | null = null) {
    return this.http.post<SiteResearch>(`${this.base}/research/start`, { siteId, recordedBy, note });
  }
  reviewQueue(segments: number[], issues: string[]) {
    let p = new HttpParams();
    segments.forEach(s => (p = p.append('segments', s)));
    issues.forEach(i => (p = p.append('issues', i)));
    return this.http.get<ReviewQueue>(`${this.base}/research/queue`, { params: p });
  }
  researchSummary(segments: number[] = []) {
    let p = new HttpParams();
    segments.forEach(s => (p = p.append('segments', s)));
    return this.http.get<ResearchSummary>(`${this.base}/research/summary`, { params: p });
  }

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
