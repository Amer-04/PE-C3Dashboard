import { ChangeDetectionStrategy, Component, computed, inject, signal, effect, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { MethodologyStore } from '../../core/methodology-store';
import {
  DEFAULT_FILTER, IOS_LABELS, IosClassification, PagedResult, SCREENING_DECISIONS, SCREENING_LABELS, ScreeningCounts,
  ScreeningDecision, ScreeningSummary, SiteDetail, SiteFilter, SiteListItem, SiteScreening,
} from '../../core/models';
import { SavedStore } from '../../core/saved-store';
import { CompareToggleComponent } from '../compare/compare-toggle.component';
import { SatelliteMapComponent } from './satellite-map.component';
import { ScreeningPanelComponent } from './screening-panel.component';

/** Plain-language status text that never presents an unchecked value as clear or verified. */
export function floodText(d: Pick<SiteDetail, 'floodZone100Yr'>): { text: string; tone: string } {
  if (d.floodZone100Yr == null) return { text: 'Not checked in source data', tone: 'unknown' };
  if (d.floodZone100Yr.toLowerCase() === 'none') return { text: 'Outside FEMA 100-yr zone (desktop screen)', tone: 'clear' };
  return { text: `In FEMA 100-yr zone ${d.floodZone100Yr}`, tone: 'flag' };
}
export function contaminationText(d: Pick<SiteDetail, 'knownContaminatedSites'>): { text: string; tone: string } {
  if (d.knownContaminatedSites == null) return { text: 'Not checked in source data', tone: 'unknown' };
  if (d.knownContaminatedSites === 0) return { text: 'No known contaminated sites (desktop screen)', tone: 'clear' };
  return { text: `${d.knownContaminatedSites} known contaminated site(s) on/near parcel`, tone: 'flag' };
}
export function verdictText(status: string, evidence: string | null): { text: string; tone: string } {
  if (status === 'Verified') return { text: evidence ? `Verified — ${evidence}` : 'Verified', tone: 'verified' };
  if (status === 'Concern') return { text: evidence ? `Concern — ${evidence}` : 'Concern', tone: 'concern' };
  return { text: 'Not reviewed', tone: 'unknown' };
}

/**
 * Focused screening workflow: work down a ranked queue of candidates, review each in satellite imagery and Street
 * View with the key property facts alongside, and record a suitability decision backed by evidence.
 */
@Component({
  selector: 'app-screening-page',
  imports: [DecimalPipe, RouterLink, SatelliteMapComponent, ScreeningPanelComponent, CompareToggleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel scr-top">
      <div class="panel-head">
        <div>
          <h2>Site screening</h2>
          <div class="muted small">Review candidates in satellite imagery and record a suitability decision with evidence. Not a sales pipeline.</div>
        </div>
        <div class="head-actions wrap">
          <a class="btn" [href]="exportQueueUrl()" download title="CSV of every site in the current queue (filters applied), with screening decisions and evidence">Export queue CSV</a>
          <a class="btn primary" [href]="exportScreenedUrl()" download title="CSV of sites with a decision (Promising / Needs more research / Unsuitable) in the current segment and rank scope">Export screened sites</a>
          <a class="btn" routerLink="/">← Dashboard</a>
        </div>
      </div>
      @if (scoped(); as s) {
        <div class="scr-scope small muted">
          Progress for <b>{{ segment() ? 'Segment ' + segment() : 'all segments' }}</b> · {{ topN() ? 'top ' + topN() + ' per segment' : 'all ranks' }}:
          <b>{{ s.reviewed | number }}</b> of {{ s.total | number }} reviewed ({{ pct(s) }}%)
        </div>
        <div class="scr-summary" role="group" aria-label="Screening progress">
          @for (o of decisionOptions; track o.token) {
            <button type="button" class="scr-stat" [attr.data-d]="o.value" [class.on]="decisions().includes(o.token)"
                    [attr.aria-pressed]="decisions().includes(o.token)" (click)="toggleDecision(o.token)" [title]="'Show ' + o.label + ' in the queue'">
              <b>{{ count(s, o.value) | number }}</b><span>{{ o.label }}</span>
            </button>
          }
        </div>
      }
      <div class="scr-controls">
        <label>Segment
          <select [value]="segment() ?? ''" (change)="setSegment($any($event.target).value)" aria-label="Segment">
            <option value="">All segments</option>
            @for (n of [1, 2, 3, 4, 5, 6]; track n) { <option [value]="n">Segment {{ n }}</option> }
          </select>
        </label>
        <label>Ranks
          <select [value]="topN() ?? ''" (change)="setTopN($any($event.target).value)" aria-label="Top N per segment">
            <option value="10">Top 10 per segment</option>
            <option value="25">Top 25 per segment</option>
            <option value="100">Top 100 per segment</option>
            <option value="">All ranks</option>
          </select>
        </label>
        <input type="search" placeholder="Search address, municipality or site ID…" aria-label="Search queue"
               [value]="search()" (input)="setSearch($any($event.target).value)" />
        @if (decisions().length) { <button type="button" class="btn-ghost small" (click)="decisions.set([]); reload()">Clear decision filter</button> }
      </div>
      @if (summary(); as sum) {
        <div class="seg-progress" role="group" aria-label="Screening progress by segment">
          @for (g of sum.segments; track g.segment) {
            <button type="button" class="seg-prog" [class.on]="segment() === g.segment" (click)="setSegment(segment() === g.segment ? '' : '' + g.segment)"
                    [title]="'Segment ' + g.segment + ': ' + g.promising + ' promising, ' + g.needsMoreResearch + ' need research, ' + g.unsuitable + ' unsuitable, ' + g.notReviewed + ' not reviewed'">
              <span class="sp-top"><b>Seg {{ g.segment }}</b><span>{{ g.reviewed | number }}/{{ g.total | number }}</span></span>
              <span class="sp-bar" aria-hidden="true">
                <i data-d="Promising" [style.width.%]="share(g.promising, g.total)"></i><i data-d="NeedsMoreResearch" [style.width.%]="share(g.needsMoreResearch, g.total)"></i><i data-d="Unsuitable" [style.width.%]="share(g.unsuitable, g.total)"></i>
              </span>
            </button>
          }
        </div>
      }
    </section>

    <div class="scr-grid">
      <aside class="panel scr-queue" aria-label="Screening queue">
        <div class="panel-head"><h2>Queue</h2><span class="muted small">{{ (queue()?.totalCount ?? 0) | number }} {{ queue()?.totalCount === 1 ? 'site' : 'sites' }} · ranked by C3 score</span></div>
        <ol class="queue-list">
          @for (s of queue()?.items ?? []; track s.siteId; let i = $index) {
            <li [class.on]="selected()?.siteId === s.siteId">
              <button type="button" (click)="open(i)">
                <span class="q-top"><span class="muted small">Seg {{ s.segment }} · #{{ s.rank }}</span>
                  <span class="decision-badge" [attr.data-d]="s.screeningDecision">{{ decisionLabel(s.screeningDecision) }}</span></span>
                <b>{{ s.address }}</b>
                <span class="muted small">{{ s.municipality }} · score {{ s.score | number: '1.1-1' }} · {{ s.openAcres | number: '1.1-1' }} open ac</span>
              </button>
            </li>
          } @empty {
            <li class="muted small q-empty">No candidates match. Clear a filter to see more.</li>
          }
        </ol>
        @if (queue(); as q) {
          <div class="pager">
            <button type="button" class="btn" [disabled]="q.page <= 1" (click)="goPage(q.page - 1)">‹</button>
            <span class="small">Page {{ q.page }} / {{ q.totalPages || 1 }}</span>
            <button type="button" class="btn" [disabled]="q.page >= q.totalPages" (click)="goPage(q.page + 1)">›</button>
          </div>
        }
      </aside>

      @if (detail(); as d) {
        <section class="panel scr-imagery" aria-label="Satellite review">
          <div class="scr-imagery-head">
            <div>
              <div class="eyebrow">Segment {{ d.summary.segment }} · rank #{{ d.summary.rank }} of {{ d.sitesInSegment | number }}</div>
              <h2>{{ d.summary.address }}</h2>
              <div class="muted small">{{ d.summary.municipality }}, {{ d.summary.county }} County · {{ d.summary.siteId }}</div>
            </div>
            <div class="scr-nav">
              <button type="button" class="btn" [disabled]="!hasPrev()" (click)="step(-1)" aria-label="Previous site">‹ Prev</button>
              <button type="button" class="btn" [disabled]="!hasNext()" (click)="step(1)" aria-label="Next site">Next ›</button>
            </div>
          </div>
          <div class="link-row">
            <a class="btn primary" [href]="d.satelliteUrl" target="_blank" rel="noopener">Satellite in Google Maps ↗</a>
            <a class="btn" [href]="d.streetViewUrl" target="_blank" rel="noopener">Street View ↗</a>
            <a class="btn" [href]="d.googleMapsUrl" target="_blank" rel="noopener">Google Maps ↗</a>
            <button type="button" class="btn save-btn" [class.on]="saved.isSaved(d.summary.siteId)" [disabled]="saved.isBusy(d.summary.siteId)"
                    [attr.aria-pressed]="saved.isSaved(d.summary.siteId)" (click)="saved.toggle(d.summary.siteId)"
                    [title]="saved.isSaved(d.summary.siteId) ? 'Remove bookmark' : 'Bookmark this site (Saved Sites)'">
              {{ saved.isSaved(d.summary.siteId) ? '★ Saved' : '☆ Save' }}
            </button>
            <app-compare-toggle [item]="{ siteId: d.summary.siteId, label: d.summary.address, segment: d.summary.segment }" />
          </div>
          <app-satellite-map [lat]="d.summary.lat" [lng]="d.summary.lng" [label]="d.summary.address" />
        </section>

        <aside class="panel scr-facts" aria-label="Property facts">
          <h3>Property facts</h3>
          <dl class="facts tone-facts">
            <dt>C3 score</dt><dd>{{ d.summary.score | number: '1.1-1' }} <small class="muted">(segment-relative)</small></dd>
            <dt>Property</dt><dd>{{ d.summary.propClass }} · {{ d.summary.acres | number: '1.0-2' }} ac total</dd>
            <dt>Usable space</dt>
            <dd [attr.data-tone]="d.summary.openAcres > 0 ? 'source' : 'unknown'">
              @if (d.summary.openAcres > 0) { ~{{ d.summary.openAcres | number: '1.0-2' }} open ac <small class="muted">(estimate)</small> }
              @else { Unknown — no open-acreage estimate }</dd>
            <dt>Parking (prelim.)</dt>
            <dd [attr.data-tone]="d.summary.estimatedParking !== null ? 'source' : 'unknown'">
              @if (d.summary.estimatedParking !== null) { ~{{ d.summary.estimatedParking | number }} trailers <small class="muted">— preliminary, not verified</small> }
              @else { Unknown }</dd>
            <dt>Verified parking</dt><dd [attr.data-tone]="v(d.parkingStatus, d.verifiedParkingEvidence).tone">{{ v(d.parkingStatus, d.verifiedParkingEvidence).text }}</dd>
            <dt>Highway &amp; exit</dt>
            <dd>@if (d.summary.nearestHighway) { {{ d.summary.nearestHighway }} exit {{ d.summary.nearestExit }} · {{ d.summary.milesToExit | number: '1.2-2' }} mi }
              @else { <span class="muted">Unknown</span> }</dd>
            <dt>Truck entrance</dt><dd [attr.data-tone]="v(d.truckEntranceStatus, d.truckEntranceEvidence).tone">{{ v(d.truckEntranceStatus, d.truckEntranceEvidence).text }}</dd>
            <dt>Electrical</dt><dd [attr.data-tone]="v(d.electricalStatus, d.electricalEvidence).tone">{{ v(d.electricalStatus, d.electricalEvidence).text }}
              <br /><small class="muted">Utility: {{ d.summary.utility || 'unknown' }}</small></dd>
            <dt>IOS</dt><dd [attr.data-tone]="d.summary.iosBasis === 'ManualReview' ? (d.summary.iosClassification === 'Confirmed' ? 'verified' : 'concern') : 'source'">
              {{ iosLabel(d.summary.iosClassification) }}
              <br /><small class="muted">{{ d.summary.iosBasis === 'ManualReview' ? 'Manual review' : 'Source data — not verified' }}</small></dd>
            <dt>Flood</dt><dd [attr.data-tone]="flood(d).tone">{{ flood(d).text }}</dd>
            <dt>Contamination</dt><dd [attr.data-tone]="contamination(d).tone">{{ contamination(d).text }}</dd>
            <dt>Highlands</dt>
            <dd [attr.data-tone]="d.highlands ? (d.summary.inHighlandsPreservation ? 'flag' : 'caution') : 'clear'">{{ d.highlands || 'No designation in source data' }}</dd>
            <dt>Business on site</dt><dd>{{ d.summary.businesses || 'None listed' }}</dd>
          </dl>

          <app-screening-panel [siteId]="d.summary.siteId" [historyOpen]="true" (saved)="onSaved($event)" />
          @if (savedNote(); as n) { <div class="small good-text" role="status">{{ n }}</div> }
        </aside>
      } @else {
        <section class="panel scr-imagery empty-state">
          <p><b>Select a candidate from the queue</b> to review it in satellite imagery.</p>
        </section>
      }
    </div>
  `,
})
export class ScreeningPage {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  protected readonly saved = inject(SavedStore);

  protected readonly decisionOptions = SCREENING_DECISIONS;
  protected readonly segment = signal<number | null>(null);
  protected readonly topN = signal<number | null>(25);
  protected readonly search = signal('');
  protected readonly decisions = signal<string[]>([]);
  protected readonly page = signal(1);

  protected readonly queue = signal<PagedResult<SiteListItem> | null>(null);
  protected readonly summary = signal<ScreeningSummary | null>(null);
  protected readonly index = signal<number | null>(null);
  protected readonly detail = signal<SiteDetail | null>(null);
  protected readonly savedNote = signal<string | null>(null);

  protected readonly selected = computed(() => {
    const i = this.index();
    return i == null ? null : this.queue()?.items[i] ?? null;
  });
  protected readonly hasPrev = computed(() => (this.index() ?? 0) > 0 || (this.queue()?.page ?? 1) > 1);
  protected readonly hasNext = computed(() => {
    const q = this.queue();
    return !!q && ((this.index() ?? -1) < q.items.length - 1 || q.page < q.totalPages);
  });

  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private pendingSelect: 'first' | 'last' | string | null = 'first';

  constructor() {
    const site = this.route.snapshot.queryParamMap.get('site');
    if (site) { this.search.set(site); this.topN.set(null); this.pendingSelect = site; }
    this.reload();
    this.loadSummary();
    effect(() => { if (this.methodology.epoch() > 0) untracked(() => { this.reload(); this.loadSummary(); }); });
  }

  private readonly methodology = inject(MethodologyStore);

  /** Counts for the current scope: the selected segment, else all segments (both already limited to top-N). */
  protected readonly scoped = computed<ScreeningCounts | null>(() => {
    const s = this.summary();
    if (!s) return null;
    const seg = this.segment();
    return seg ? s.segments.find(g => g.segment === seg) ?? null : s;
  });
  protected readonly exportQueueUrl = computed(() => this.api.exportUrl(this.queueFilter()));
  protected readonly exportScreenedUrl = computed(() =>
    this.api.exportUrl({ ...this.queueFilter(), search: null, screeningDecisions: ['promising', 'research', 'unsuitable'] }));

  protected pct(s: ScreeningCounts) { return s.total ? Math.round((100 * s.reviewed) / s.total) : 0; }
  protected share(n: number, total: number) { return total ? (100 * n) / total : 0; }

  protected count(s: ScreeningCounts, d: ScreeningDecision) {
    return d === 'Promising' ? s.promising : d === 'NeedsMoreResearch' ? s.needsMoreResearch : d === 'Unsuitable' ? s.unsuitable : s.notReviewed;
  }
  protected decisionLabel(d: ScreeningDecision) { return SCREENING_LABELS[d] ?? d; }
  protected iosLabel(c: IosClassification) { return IOS_LABELS[c] ?? c; }
  protected v = verdictText;
  protected flood = floodText;
  protected contamination = contaminationText;

  protected toggleDecision(token: string) {
    this.decisions.update(cur => cur.includes(token) ? cur.filter(t => t !== token) : [...cur, token]);
    this.reload();
  }
  protected setSegment(v: string) { this.segment.set(v ? +v : null); this.reload(); }
  protected setTopN(v: string) { this.topN.set(v ? +v : null); this.reload(); this.loadSummary(); }
  protected setSearch(v: string) {
    this.search.set(v);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.reload(), 300);
  }
  protected goPage(p: number, select: 'first' | 'last' = 'first') { this.page.set(p); this.fetch(select); }

  /** Re-query from page 1 and select the first site. */
  reload() { this.page.set(1); this.fetch(this.pendingSelect ?? 'first'); this.pendingSelect = null; }

  protected open(i: number) {
    this.index.set(i);
    this.savedNote.set(null);
    const item = this.queue()?.items[i];
    if (!item) { this.detail.set(null); return; }
    this.api.site(item.id).subscribe({ next: d => this.detail.set(d), error: () => this.detail.set(null) });
  }

  protected step(delta: 1 | -1) {
    const q = this.queue();
    const i = this.index();
    if (!q || i == null) return;
    const next = i + delta;
    if (next >= 0 && next < q.items.length) this.open(next);
    else if (delta > 0 && q.page < q.totalPages) this.goPage(q.page + 1, 'first');
    else if (delta < 0 && q.page > 1) this.goPage(q.page - 1, 'last');
  }

  protected onSaved(s: SiteScreening) {
    // Update the queue badge + the fact sheet in place; refresh the counts.
    this.queue.update(q => q && { ...q, items: q.items.map(it => it.siteId === s.siteId ? { ...it, screeningDecision: s.decision } : it) });
    this.detail.update(d => d && d.summary.siteId === s.siteId
      ? { ...d, summary: { ...d.summary, screeningDecision: s.decision }, screeningEvidence: s.evidence, screeningUpdatedAtUtc: s.updatedAtUtc } : d);
    this.savedNote.set(`Saved: ${SCREENING_LABELS[s.decision]}. Use Next › to continue.`);
    this.loadSummary();
  }

  /** The queue's filter (also used for the CSV exports). */
  protected queueFilter(): SiteFilter {
    return {
      ...DEFAULT_FILTER,
      segments: this.segment() ? [this.segment()!] : [],
      topN: this.topN(),
      search: this.search().trim() || null,
      screeningDecisions: this.decisions(),
    };
  }

  private fetch(select: 'first' | 'last' | string) {
    this.api.sites(this.queueFilter(), this.page(), 25, 'Score', true).subscribe({
      next: q => {
        this.queue.set(q);
        const i = select === 'first' ? 0 : select === 'last' ? q.items.length - 1 : Math.max(0, q.items.findIndex(x => x.siteId === select));
        if (q.items.length) this.open(i);
        else { this.index.set(null); this.detail.set(null); }
      },
      error: () => this.queue.set(null),
    });
  }

  private loadSummary() { this.api.screeningSummary(this.topN()).subscribe({ next: s => this.summary.set(s), error: () => {} }); }
}
