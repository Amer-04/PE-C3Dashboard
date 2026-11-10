import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { SiteStore } from '../../core/site-store';
import { MethodologyStore } from '../../core/methodology-store';
import { Activation, Criterion, ScoringFieldInfo, VersionDetail, VersionSummary } from '../../core/methodology.models';
import { explainScoring } from './criterion-text';
import { problem } from './publish-dialog.component';
import { CustomFieldsPanelComponent } from './custom-fields-panel.component';
import { CriteriaBoardComponent, CriterionCardItem } from './criteria-board.component';

type Tab = 'criteria' | 'draft' | 'history' | 'fields' | 'reference';
/** Top-level navigation: Data Fields and Reference live under Advanced Options (their ?tab= links still work). */
type Primary = 'criteria' | 'draft' | 'history' | 'advanced';
const TABS: Tab[] = ['criteria', 'draft', 'history', 'fields', 'reference'];

/**
 * Methodology Manager overview: the active methodology at a glance, then tabs for its criteria (cards), the working
 * draft & preview, version history, data fields and the workbook reference notes. Nothing on this page changes live
 * scores — Edit / Add open the working draft (never the published version), and only a previewed, confirmed publish
 * in the draft workspace switches scoring.
 */
@Component({
  selector: 'app-methodology-page',
  imports: [DecimalPipe, DatePipe, RouterLink, CustomFieldsPanelComponent, CriteriaBoardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mm">
      <!-- ===== Header ===== -->
      <section class="panel mm-hero" aria-labelledby="mm-title">
        <div class="mm-hero-top">
          <div>
            <h1 id="mm-title">Site Scoring Rules</h1>
            <p class="mm-hero-sub">These rules determine how potential truck charging locations are evaluated and ranked.</p>
          </div>
          <button type="button" class="btn primary lg" (click)="addCriterion()" [disabled]="busy()">
            <span aria-hidden="true">＋</span> Add Rule</button>
        </div>
        @if (error()) { <p class="mm-note bad" role="alert">{{ error() }}</p> }

        <div class="mm-status">
          <div class="mm-status-item">
            <div class="mm-stat-label">Active scoring method</div>
            @if (methodology.active(); as a) {
              <div class="mm-status-value">{{ a.version.name }}</div>
              <div class="mm-status-note">Version {{ a.version.id }} · {{ enabledCount() }} of {{ criteriaCount() }} rules turned on</div>
            } @else { <div class="mm-status-value mm-faint">Loading…</div> }
          </div>
          <div class="mm-status-item" [class.pending]="!!openDraft()">
            <div class="mm-stat-label">Unapplied changes</div>
            @if (openDraft(); as d) {
              <div class="mm-status-value">Draft in progress</div>
              <div class="mm-status-note">{{ d.previewCurrent ? 'Results reviewed — ready to apply.' : 'Results not reviewed yet.' }}
                </div>
              <button type="button" class="btn rl-btn mm-status-btn" (click)="go('draft')">Review Changes</button>
            } @else {
              <div class="mm-status-value">None</div>
              <div class="mm-status-note">Live scores match the active scoring method.</div>
            }
          </div>
        </div>

        <nav class="mm-tabs" role="tablist" aria-label="Scoring rules sections">
          @for (t of tabs; track t.id) {
            <button type="button" role="tab" class="mm-tab" [class.on]="primary() === t.id" [attr.aria-selected]="primary() === t.id"
                    [id]="'mm-tab-' + t.id" (click)="go(t.id)">
              {{ t.label }}
              @if (t.id === 'criteria') { <span class="count">{{ criteriaCount() }}</span> }
              @if (t.id === 'draft' && openDraft()) { <span class="dot" title="Unapplied changes"></span> }
              @if (t.id === 'history') { <span class="count">{{ versions().length }}</span> }
            </button>
          }
        </nav>
      </section>

      @if (primary() === 'advanced') {
        <section class="panel mm-adv" aria-labelledby="mm-tab-advanced">
          <div class="mm-section-head">
            <div>
              <h2>Advanced options</h2>
              <p>The data fields rules can use, and notes on how the original workbook scores are reproduced.</p>
            </div>
          </div>
          <div class="mm-subtabs" role="group" aria-label="Advanced options">
            <button type="button" [class.on]="tab() === 'fields'" [attr.aria-pressed]="tab() === 'fields'" (click)="go('fields')">Data Fields</button>
            <button type="button" [class.on]="tab() === 'reference'" [attr.aria-pressed]="tab() === 'reference'" (click)="go('reference')">Reference</button>
          </div>
        </section>
      }

      <!-- ===== Scoring criteria ===== -->
      @switch (tab()) {
        @case ('criteria') {
          <section class="panel" role="tabpanel" aria-labelledby="mm-tab-criteria">
            <div class="mm-section-head">
              <div>
                <h2>Scoring rules</h2>
                <p>Select <b>Show More</b> to see exactly how a rule awards points. <b>Edit Rule</b> opens a draft copy — live scores
                  don't change until you apply scoring.</p>
              </div>
            </div>
            @if (methodology.active()) {
              <app-criteria-board [items]="cardItems()" [busy]="busy()" (edit)="editCriterion($event)" />
            } @else {
              <p class="mm-muted">Loading the scoring rules…</p>
            }
          </section>
        }

        <!-- ===== Draft & preview ===== -->
        @case ('draft') {
          <section class="panel" role="tabpanel" aria-labelledby="mm-tab-draft">
            <div class="mm-section-head">
              <div>
                <h2>Review changes</h2>
                <p>Drafts never affect live scores. Save the draft, review results for all {{ totalSites() | number }} sites, then confirm
                  <b>Apply Scoring</b> — every view switches to the new scores at once.</p>
              </div>
            </div>
            @if (openDraft(); as d) {
              <div class="mm-version" style="margin-bottom:12px">
                <div class="vno">v{{ d.id }}</div>
                <div>
                  <div class="vtitle">{{ d.name }} <span class="pill amber">Draft</span>
                    @if (d.previewCurrent) { <span class="pill blue">Results reviewed</span> }
                    @if (d.identicalToBaseline) { <span class="pill violet">= original</span> }</div>
                  <div class="vmeta">Based on v{{ d.basedOnVersionId }} · {{ d.enabledCount }} of {{ d.criteriaCount }} rules on · last saved {{ d.updatedAtUtc | date: 'medium' }}</div>
                </div>
                <a class="btn primary" [routerLink]="['/methodology/versions', d.id]">Open draft workspace</a>
              </div>
              <ol class="mm-steps" aria-label="Publishing workflow">
                <li class="done"><span class="n">✓</span><span class="t"><b>Edit rules</b>Draft created</span></li>
                <li class="done"><span class="n">✓</span><span class="t"><b>Save draft</b>Saved {{ d.updatedAtUtc | date: 'shortTime' }}</span></li>
                <li [class.done]="d.previewCurrent" [class.current]="!d.previewCurrent"><span class="n">{{ d.previewCurrent ? '✓' : '3' }}</span>
                  <span class="t"><b>Review Results</b>{{ d.previewCurrent ? 'Up to date' : 'Not run yet' }}</span></li>
                <li [class.current]="d.previewCurrent"><span class="n">4</span><span class="t"><b>Apply Scoring</b>Typed confirmation</span></li>
              </ol>
              <h3>Changes compared with the active scoring method</h3>
              @if (draftDetail(); as dd) {
                @if (dd.changesVsActive.length) {
                  <ul class="mm-changes">
                    @for (c of dd.changesVsActive; track c.key) {
                      <li><span class="pill" [class.green]="c.change === 'Added' || c.change === 'Enabled'"
                                [class.red]="c.change === 'Removed' || c.change === 'Disabled'" [class.blue]="c.change === 'Modified'">{{ c.change }}</span>
                        <b>{{ c.name }}</b>
                        @for (x of c.details; track $index) { <div class="mm-xs mm-muted">{{ x }}</div> }</li>
                    }
                  </ul>
                } @else { <p class="mm-muted mm-xs">No saved differences yet — the draft has the same rules as the active version.</p> }
              } @else { <p class="mm-muted mm-xs">Loading…</p> }
            } @else {
              <div class="mm-empty">
                <b>No draft in progress</b>
                <span>Use <b>Edit Rule</b> on any rule or <b>Add Rule</b> to start one. The active version stays live until you apply scoring.</span>
                <button type="button" class="btn" (click)="startDraft()" [disabled]="busy()">Start a draft from v{{ methodology.versionId() }}</button>
              </div>
            }
            @if (otherDrafts().length) {
              <h3>Other drafts</h3>
              <div class="mm-versions">
                @for (v of otherDrafts(); track v.id) {
                  <div class="mm-version">
                    <div class="vno">v{{ v.id }}</div>
                    <div><div class="vtitle">{{ v.name }} <span class="pill amber">Draft</span></div>
                      <div class="vmeta">Based on v{{ v.basedOnVersionId }} (not the active version) · updated {{ v.updatedAtUtc | date: 'medium' }}</div></div>
                    <a class="btn small" [routerLink]="['/methodology/versions', v.id]">Open</a>
                  </div>
                }
              </div>
            }
          </section>
        }

        <!-- ===== Version history ===== -->
        @case ('history') {
          <section class="panel" role="tabpanel" aria-labelledby="mm-tab-history">
            <div class="mm-section-head">
              <div>
                <h2>Previous methods</h2>
                <p>Every scoring method that has been applied is kept and can be restored at any time (review results, then confirm).
                  Version 1 is the original C3 workbook method and can never be edited or deleted.</p>
              </div>
            </div>
            <div class="mm-versions">
              @for (v of versionsNewestFirst(); track v.id) {
                <div class="mm-version" [class.active]="v.isActive">
                  <div class="vno">v{{ v.id }}</div>
                  <div>
                    <div class="vtitle">{{ v.name }}
                      @if (v.isActive) { <span class="pill green"><span class="dot"></span>Active</span> }
                      @if (v.status === 'Draft') { <span class="pill amber">Draft</span> }
                      @if (v.isPreviousActive) { <span class="pill">Previous</span> }
                      @if (v.isBaseline) { <span class="pill violet">Original baseline</span> } @else if (v.identicalToBaseline) { <span class="pill violet">= original</span> }
                    </div>
                    <div class="vmeta">Version {{ v.id }} · {{ v.enabledCount }} of {{ v.criteriaCount }} rules on ·
                      @if (v.publishedAtUtc) { published {{ v.publishedAtUtc | date: 'medium' }} } @else { created {{ v.createdAtUtc | date: 'medium' }} }</div>
                    @if (v.changeSummary) { <div class="vsum" [title]="v.changeSummary">{{ v.changeSummary }}</div> }
                  </div>
                  <a class="btn small" [routerLink]="['/methodology/versions', v.id]">
                    {{ v.status === 'Draft' ? 'Open draft' : v.isActive ? 'View' : 'View / restore' }}</a>
                </div>
              }
            </div>
            @if (activations().length) {
              <details class="mm-ref" style="margin-top:12px">
                <summary>Change log <small>{{ activations().length }} event(s) — every time live scoring switched</small></summary>
                <div class="mm-ref-body">
                  <ul class="mm-log">
                    @for (a of activations(); track a.id) {
                      <li><span class="mm-faint">{{ a.atUtc | date: 'medium' }}</span> — <b>{{ a.reason }}</b> v{{ a.versionId }} “{{ a.versionName }}”
                        @if (a.previousVersionId && a.previousVersionId !== a.versionId) { (was v{{ a.previousVersionId }}) }
                        · {{ a.sitesScored | number }} sites @if (a.note) { · {{ a.note }} }</li>
                    }
                  </ul>
                </div>
              </details>
            }
          </section>
        }

        <!-- ===== Data fields ===== -->
        @case ('fields') {
          <section class="panel" role="tabpanel" aria-labelledby="mm-tab-advanced">
            <app-custom-fields-panel (addCustom)="addCustom()" />
            <details class="mm-ref" style="margin-top:16px">
              <summary>Built-in data fields <small>{{ builtinFields().length }} fields any rule can use</small></summary>
              <div class="mm-ref-body">
                <div class="mm-toolbar" style="margin:0 0 8px">
                  <label class="mm-search">
                    <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="9" cy="9" r="6" /><path d="m14 14 4 4" /></svg>
                    <input type="search" placeholder="Search data fields" aria-label="Search data fields" (input)="fieldQuery.set(val($event))" />
                  </label>
                </div>
                <div class="mm-fields-grid">
                  @for (f of filteredBuiltin(); track f.key) {
                    <div class="mm-fieldrow">
                      <div class="nm">{{ f.label }} <span class="mm-faint mm-xs">{{ f.group }} · {{ typeLabel(f) }}</span></div>
                      <div class="ds">{{ f.description }}</div>
                      <div class="cov" [title]="(f.present | number) + ' of ' + ((f.present + f.missing) | number) + ' sites have a value'">
                        <span class="mm-cov-bar"><i [style.width.%]="coverage(f)"></i></span>{{ coverage(f) | number: '1.0-0' }}% have data</div>
                    </div>
                  } @empty { <p class="mm-muted">No data fields match.</p> }
                </div>
              </div>
            </details>
          </section>
        }

        <!-- ===== Reference ===== -->
        @case ('reference') {
          <section role="tabpanel" aria-labelledby="mm-tab-advanced">
            <details class="mm-ref" open>
              <summary>How the original workbook rules are reproduced <small>Version 1 reproduces the workbook exactly</small></summary>
              <div class="mm-ref-body">
                <ul>
                  <li><b>Unchecked flood status:</b> only the top ~150 per segment were checked against FEMA maps; the original methodology's
                    missing-data policy gives every unchecked site the −10 flood penalty so it stays below the verified shortlist.</li>
                  <li><b>Busy multi-tenant:</b> the workbook lists at most three business names, not the count, so the importer infers this flag
                    from the delivered score.</li>
                  <li><b>Rank ties</b> are broken by distance to the exit, then the workbook's own rank, then Site ID — so equal scores always rank the
                    same way. Version 1 reproduces the workbook score and rank of every one of the {{ totalSites() | number }} sites.</li>
                  @if (methodology.active(); as a) { <li><b>Tie-break order:</b> {{ a.tieBreak }}</li> }
                </ul>
              </div>
            </details>
            <details class="mm-ref">
              <summary>Who we are siting for <small>Power Edison &amp; EV Edison</small></summary>
              <div class="mm-ref-body">
                <div class="about-grid">
                  <div>
                    <h3>Power Edison</h3>
                    <p>New Jersey clean-energy company (CEO Dr. Shihab Kuran) that builds utility-grade <b>mobile battery
                      storage</b> — the trailer-based TerraCharge platform with separate battery and power-conversion trailers
                      (up to 33 kV). Mobile storage lets a charging site open <b>before slow utility grid upgrades</b> are finished,
                      and supports peak shaving, back-up power and EV charging.</p>
                  </div>
                  <div>
                    <h3>EV Edison</h3>
                    <p>Power Edison's charging-hub business. Its flagship <b>Kearny Point hub</b> (near Port Newark, opened 2025)
                      is billed as the largest multi-class charging hub on the East Coast: 30 × 180 kW ports and about 200
                      heavy-duty sessions a day in Phase I, with Einride staging electric trucks there. The pipeline includes
                      Irvington and Edison (letters of intent) and Paterson.</p>
                  </div>
                </div>
                <h3>Power Edison sites on the map</h3>
                <div class="table-scroll">
                  <table class="mini">
                    <thead><tr><th>Site</th><th>Status</th><th class="num">Segment</th><th class="num">Candidates ≤ 2 mi</th></tr></thead>
                    <tbody>
                      @for (p of store.powerEdisonSites(); track p.id) {
                        <tr><td><b>{{ p.name }}</b><br /><small class="muted">{{ p.notes }}</small></td><td>{{ p.status === 'LetterOfIntent' ? 'Letter of intent' : p.status }}</td>
                          <td class="num">{{ p.segment }}</td><td class="num">{{ p.candidatesWithinTwoMiles | number }}</td></tr>
                      }
                    </tbody>
                  </table>
                </div>
                <p class="mm-xs" style="margin-top:8px">NJDEP may fund only one of two strong proposals in close proximity, so candidates within 2 mi of a
                  Power Edison site are flagged rather than penalised.</p>
              </div>
            </details>
            <details class="mm-ref">
              <summary>Data sources &amp; method notes <small>{{ (notes() ?? []).length }} notes from the C3 workbook</small></summary>
              <div class="mm-ref-body">
                <dl class="notes">
                  @for (n of notes() ?? []; track n.id) { <dt>{{ n.topic }}</dt><dd>{{ n.detail }}</dd> }
                </dl>
              </div>
            </details>
          </section>
        }
      }
    </div>
  `,
  styles: [`
    .mm-changes { list-style: none; padding: 0; margin: 0; display: grid; gap: 8px; }
    .mm-changes li { padding: 8px 10px; border: 1px solid var(--border); border-radius: 8px; font-size: 13px; }
    .mm-changes .pill { margin-right: 6px; }
    .mm-log { margin: 0; padding-left: 18px; display: grid; gap: 4px; }
    .mm-fields-grid { display: grid; gap: 2px; }
    .mm-fieldrow { display: grid; grid-template-columns: minmax(0, 1fr) 150px; gap: 0 12px; padding: 7px 4px; border-bottom: 1px solid var(--border); align-items: center; }
    .mm-fieldrow:last-child { border-bottom: 0; }
    .mm-fieldrow .nm { font-weight: 600; color: var(--text); font-size: 13px; }
    .mm-fieldrow .ds { grid-column: 1; font-size: 12px; }
    .mm-fieldrow .cov { grid-row: 1 / span 2; grid-column: 2; display: flex; gap: 6px; align-items: center; font-size: 11.5px; justify-content: flex-end; }
    .about-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
    .table-scroll { overflow-x: auto; }
    h3 { margin: 16px 0 8px; }
    @media (max-width: 640px) {
      .about-grid { grid-template-columns: 1fr; }
      .mm-fieldrow { grid-template-columns: minmax(0, 1fr); }
      .mm-fieldrow .cov { grid-row: auto; grid-column: 1; justify-content: flex-start; margin-top: 3px; }
    }
  `],
})
export class MethodologyPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly store = inject(SiteStore);
  protected readonly methodology = inject(MethodologyStore);

  /** ?tab=criteria|draft|history|fields|reference */
  readonly tabParam = input<string | undefined>(undefined, { alias: 'tab' });

  protected readonly tabs: { id: Primary; label: string }[] = [
    { id: 'criteria', label: 'Scoring Rules' }, { id: 'draft', label: 'Review Changes' }, { id: 'history', label: 'Previous Methods' },
    { id: 'advanced', label: 'Advanced Options' },
  ];
  protected readonly tab = computed<Tab>(() => {
    const t = this.tabParam() === 'advanced' ? 'fields' : this.tabParam() ?? '';
    return (TABS as string[]).includes(t) ? t as Tab : 'criteria';
  });
  protected readonly primary = computed<Primary>(() => { const t = this.tab(); return t === 'fields' || t === 'reference' ? 'advanced' : t; });

  protected readonly notes = toSignal(this.api.methodology());
  protected readonly versions = signal<VersionSummary[]>([]);
  protected readonly activations = signal<Activation[]>([]);
  protected readonly fields = signal<ScoringFieldInfo[]>([]);
  protected readonly draftDetail = signal<VersionDetail | null>(null);
  /** The active version's structured rules, used only to explain each rule exactly (Show More). */
  private readonly activeRules = signal<{ id: number; criteria: Criterion[] } | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldQuery = signal('');

  constructor() {
    // Reload history (and field coverage) whenever the active version changes (publish here or elsewhere).
    effect(() => { this.methodology.active(); untracked(() => this.loadHistory()); });
    effect(() => {
      const id = this.methodology.versionId();
      untracked(() => {
        if (id !== null && this.activeRules()?.id !== id)
          this.api.methodologyVersion(id).subscribe({ next: d => this.activeRules.set({ id, criteria: d.rules.criteria }), error: () => {} });
      });
    });
    // The open draft's saved differences, for the Draft & Preview tab.
    effect(() => {
      const d = this.openDraft();
      const wanted = this.tab() === 'draft';
      untracked(() => {
        if (!d) { this.draftDetail.set(null); return; }
        if (wanted && this.draftDetail()?.version.id !== d.id)
          this.api.methodologyVersion(d.id).subscribe({ next: x => this.draftDetail.set(x), error: () => {} });
      });
    });
  }

  private loadHistory() {
    this.api.methodologyVersions().subscribe({ next: v => this.versions.set(v), error: () => {} });
    this.api.methodologyActivations().subscribe({ next: a => this.activations.set(a), error: () => {} });
    this.api.methodologyFields().subscribe({ next: f => this.fields.set(f), error: () => {} });
  }

  protected readonly cardItems = computed<CriterionCardItem[]>(() => (this.methodology.active()?.criteria ?? []).map(c => ({
    key: c.key, name: c.name, explanation: c.explanation, category: c.category, enabled: c.enabled, builtIn: c.builtIn,
    min: c.min, max: c.max, rule: c.rule,
    missingPolicy: c.missingPolicy ? c.missingPolicy.replace(/^Sites without data get /, '').replace(/ points$/, ' pts') : null,
    breakdown: this.explain(c.key),
  })));
  private explain(key: string) {
    const rules = this.activeRules(), fields = this.fields();
    if (!rules || rules.id !== this.methodology.versionId() || !fields.length) return null;
    const r = rules.criteria.find(x => x.key === key);
    return r ? explainScoring(r, fields) : null;
  }
  protected readonly criteriaCount = computed(() => this.methodology.active()?.criteria.length ?? 0);
  protected readonly enabledCount = computed(() => (this.methodology.active()?.criteria ?? []).filter(c => c.enabled).length);
  protected readonly totalSites = computed(() => { const f = this.fields()[0]; return f ? f.present + f.missing : 0; });
  protected readonly versionsNewestFirst = computed(() => [...this.versions()].sort((a, b) => b.id - a.id));
  /** The newest unpublished draft built on the active version (edits continue there). */
  protected readonly openDraft = computed(() =>
    [...this.versions()].sort((a, b) => b.id - a.id).find(v => v.status === 'Draft' && v.basedOnVersionId === this.methodology.versionId()) ?? null);
  protected readonly otherDrafts = computed(() => this.versions().filter(v => v.status === 'Draft' && v.id !== this.openDraft()?.id));
  protected readonly builtinFields = computed(() => this.fields().filter(f => !f.isCustom));
  protected readonly filteredBuiltin = computed(() => {
    const q = this.fieldQuery().trim().toLowerCase();
    return this.builtinFields().filter(f => !q || [f.label, f.description, f.group].some(t => t.toLowerCase().includes(q)));
  });

  protected go(t: Tab | Primary) {
    const to = t === 'advanced' ? 'fields' : t;
    this.router.navigate([], { queryParams: { tab: to === 'criteria' ? null : to }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  /** Edit never changes a published version (incl. the original v1): it opens the working draft based on it. */
  protected editCriterion(key: string) { this.toDraft({ edit: key }); }
  protected addCriterion() { this.toDraft({ add: '1' }); }
  protected startDraft() { this.toDraft({}); }
  protected addCustom() { this.toDraft({ add: 'custom' }); }

  private toDraft(queryParams: Record<string, string>) {
    this.busy.set(true);
    this.error.set(null);
    this.methodology.workingDraft().subscribe({
      next: id => { this.busy.set(false); this.router.navigate(['/methodology/versions', id], { queryParams }); },
      error: (e: HttpErrorResponse) => { this.busy.set(false); this.error.set(problem(e)); },
    });
  }

  protected coverage(f: ScoringFieldInfo) { const t = f.present + f.missing; return t ? (100 * f.present) / t : 0; }
  protected typeLabel(f: ScoringFieldInfo) {
    return f.type === 'Number' ? 'number' + (f.unit ? ` (${f.unit})` : '') : f.type === 'Boolean' ? 'yes / no' : 'category';
  }
  protected val(e: Event) { return (e.target as HTMLInputElement).value; }
}
