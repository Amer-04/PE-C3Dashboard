import { ChangeDetectionStrategy, Component, computed, effect, inject, input, numberAttribute, signal, untracked } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { MethodologyStore } from '../../core/methodology-store';
import {
  Criterion, MethodologyRules, PreviewSummary, PublishResult, ScoringFieldInfo, ValidationIssue, ValidationResult, VersionDetail,
} from '../../core/methodology.models';
import { CriteriaBoardComponent } from './criteria-board.component';
import { CriterionDialogComponent } from './criterion-dialog.component';
import { cardItem } from './criterion-text';
import { PreviewPanelComponent } from './preview-panel.component';
import { PublishDialogComponent, problem } from './publish-dialog.component';

/**
 * Draft workspace for one methodology version. Drafts: Edit / Add criteria (side drawer) → Save draft → Preview all
 * sites → confirm Publish. Published versions (incl. the immutable original Version 1) are read-only: "Edit" opens a
 * working draft based on them, and they can be restored through the same preview + confirmed publish. Nothing
 * recalculates on an edit.
 */
@Component({
  selector: 'app-methodology-editor-page',
  imports: [DecimalPipe, DatePipe, RouterLink, CriteriaBoardComponent, CriterionDialogComponent, PreviewPanelComponent, PublishDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mm">
      <nav class="crumbs" aria-label="Breadcrumb"><a routerLink="/methodology">Site Scoring Rules</a> <span aria-hidden="true">›</span>
        <a routerLink="/methodology" [queryParams]="{ tab: 'history' }">Previous Methods</a> <span aria-hidden="true">›</span>
        <span>v{{ id() }}</span></nav>

      @if (detail(); as d) {
        <!-- ===== Version header ===== -->
        <section class="panel head">
          <div class="head-top">
            <div class="vno" [class.draft]="d.version.status === 'Draft'" [class.active]="d.version.isActive">v{{ d.version.id }}</div>
            <div class="head-main">
              @if (editable()) {
                <input class="title" [value]="name()" maxlength="120" aria-label="Version name" (input)="name.set(val($event))" />
              } @else { <h1 class="title-ro">{{ d.version.name }}</h1> }
              <div class="badges">
                <span class="pill" [class.amber]="d.version.status === 'Draft'" [class.blue]="d.version.status === 'Published'">{{ d.version.status }}</span>
                @if (d.version.isActive) { <span class="pill green"><span class="dot"></span>Active</span> }
                @if (d.version.isPreviousActive) { <span class="pill">Previous active</span> }
                @if (d.version.isBaseline) { <span class="pill violet">Original baseline · read-only</span> }
                @else if (d.version.identicalToBaseline) { <span class="pill violet">Identical to the original</span> }
                @if (d.version.basedOnVersionId) { <span class="mm-xs mm-muted">based on v{{ d.version.basedOnVersionId }}</span> }
                <span class="mm-xs mm-muted">· updated {{ d.version.updatedAtUtc | date: 'medium' }}</span>
              </div>
            </div>
            @if (d.version.status === 'Draft') {
              <button type="button" class="btn-ghost small mm-danger" [disabled]="!!busy()" (click)="deleteDraft()">Delete draft</button>
            }
          </div>

          @if (editable()) {
            <label class="mm-input"><span>Change summary <span class="help">— shown in Previous Methods</span></span>
              <textarea rows="2" maxlength="4000" [value]="summary()" (input)="summary.set(val($event))" aria-label="Change summary"
                        placeholder="What changed and why, e.g. “Raise the open-land threshold to 4 acres”"></textarea>
            </label>
            <ol class="mm-steps" aria-label="Publishing workflow">
              @for (s of steps(); track s.n) {
                <li [class.done]="s.state === 'done'" [class.current]="s.state === 'current'" [attr.aria-current]="s.state === 'current' ? 'step' : null">
                  <span class="n">{{ s.state === 'done' ? '✓' : s.n }}</span><span class="t"><b>{{ s.title }}</b>{{ s.note }}</span></li>
              }
            </ol>
          } @else {
            @if (d.version.changeSummary) { <p class="mm-muted">{{ d.version.changeSummary }}</p> }
            <p class="mm-note">
              @if (d.version.isActive) { This scoring method is live — every dashboard view shows its scores. Applied methods never change: use <b>Edit Rule</b> on a rule to start a draft based on it. }
              @else { Applied methods never change. Use <b>Edit Rule</b> to start a draft based on this method, or <b>Review Results</b> then <b>Restore</b> to make it live again. }
            </p>
          }
        </section>

        <!-- ===== Sticky actions ===== -->
        <div class="mm-actionbar" role="toolbar" aria-label="Draft actions">
          <button type="button" class="btn" (click)="openAdd()" [disabled]="!!busy()">＋ Add Rule</button>
          @if (editable()) {
            <button type="button" class="btn primary" [disabled]="!dirty() || !!busy() || !valid()" (click)="save()">
              {{ busy() === 'save' ? 'Saving…' : dirty() ? 'Save methodology as draft' : 'Draft saved ✓' }}
            </button>
          }
          @if (!d.version.isActive) {
            <button type="button" class="btn" [class.primary]="!editable() || (!dirty() && !previewShown())" [disabled]="dirty() || !!busy() || !valid()" (click)="runPreview()"
                    [title]="dirty() ? 'Save the draft first' : 'Recalculate proposed scores and ranks for every site'">
              {{ busy() === 'preview' ? 'Recalculating all sites…' : 'Review Results' }}
            </button>
            <label class="topn mm-xs">Top <input type="number" min="1" max="50" [value]="topN()" (change)="topN.set(+val($event) || 10)" aria-label="Top N per segment" /> per segment</label>
            <button type="button" class="btn primary" [disabled]="!publishable()" (click)="dialog.set(true)"
                    [title]="publishable() ? '' : 'Review the results of the saved rules first'">
              {{ d.version.status === 'Published' ? 'Restore this version…' : 'Apply Scoring…' }}
            </button>
          }
          <span class="grow"></span>
          <span class="mm-valid" aria-live="polite">
            @if (validation(); as v) {
              <span>Possible total <b>{{ v.minPossible | number: '1.0-1' }} … {{ v.maxPossible | number: '1.0-1' }}</b></span>
              <span><b>{{ enabledCount() }}</b> of {{ rules()?.criteria?.length }} on</span>
              @if (errorCount()) { <span class="mm-bad">✕ {{ errorCount() }} error(s) — fix before saving</span> } @else { <span class="mm-ok">✓ valid</span> }
            }
          </span>
        </div>
        @if (message(); as m) { <p class="mm-note msg" [class.good]="!m.error" [class.bad]="m.error" role="status">{{ m.text }}</p> }
        @for (i of globalIssues(); track $index) { <p class="mm-note" [class.bad]="i.severity === 'Error'" [class.warn]="i.severity === 'Warning'">{{ i.message }}</p> }

        @if (d.changesVsActive.length && !d.version.isActive) {
          <details class="mm-ref diff">
            <summary>Saved differences vs the active methodology <small>{{ d.changesVsActive.length }} change(s)</small></summary>
            <div class="mm-ref-body">
              <ul class="changes">
                @for (c of d.changesVsActive; track c.key) {
                  <li><span class="pill" [class.green]="c.change === 'Added' || c.change === 'Enabled'" [class.red]="c.change === 'Removed' || c.change === 'Disabled'"
                            [class.blue]="c.change === 'Modified'">{{ c.change }}</span> <b>{{ c.name }}</b>
                    @for (x of c.details; track $index) { <div class="mm-xs mm-muted">{{ x }}</div> }</li>
                }
              </ul>
            </div>
          </details>
        }

        <!-- ===== Criteria ===== -->
        <section class="panel">
          <div class="mm-section-head">
            <div>
              <h2>Scoring rules</h2>
              <p>{{ editable() ? 'Edit Rule opens a side panel; switches turn rules on or off in this draft. Nothing is recalculated until you review results.'
                                : 'Read-only. Edit Rule opens a working draft based on this version.' }}</p>
            </div>
          </div>
          <app-criteria-board [items]="cards()" [editable]="editable()" [busy]="!!busy()" [changedKeys]="changedKeys()" [issues]="issuesByKey()"
                              (edit)="openEdit($event)" (toggle)="toggle($event)" (remove)="removeCriterion($event)" />
        </section>

        <!-- ===== Preview ===== -->
        @if (preview(); as p) {
          @if (previewShown()) {
            <app-preview-panel [preview]="p" />
          } @else if (p.rulesHash !== d.version.rulesHash) {
            <p class="mm-note warn">These results were reviewed for earlier rules. Select <b>Review Results</b> again before applying scoring.</p>
          } @else {
            <p class="mm-note warn">These results were compared with version {{ p.comparedToVersionId }}, but version {{ store.versionId() }} is active now.
              Select <b>Review Results</b> again to compare with the current scores.</p>
          }
        }
        @if (dialog() && preview(); as p) {
          <app-publish-dialog [preview]="p" [restore]="d.version.status === 'Published'"
                              (published)="onPublished($event)" (cancel)="dialog.set(false)" />
        }
        @if (editing(); as e) {
          <app-criterion-dialog [mode]="e.mode" [criterion]="e.criterion" [fields]="fields()" [existingKeys]="keys()" [startCustom]="e.custom"
                                (done)="applyDialog($event)" (cancel)="editing.set(null)" (fieldsChanged)="reloadFields()" />
        }
      } @else if (loadError()) {
        <p class="mm-note bad">{{ loadError() }}</p>
      } @else { <p class="panel mm-muted">Loading…</p> }
    </div>
  `,
  styles: [`
    .crumbs { font-size: 13px; color: var(--text-3); display: flex; gap: 6px; flex-wrap: wrap; }
    .head { display: grid; gap: 14px; }
    .head-top { display: flex; gap: 14px; align-items: flex-start; }
    .head-main { flex: 1; min-width: 0; }
    .vno { flex: none; width: 48px; height: 48px; border-radius: 12px; display: grid; place-items: center; font-weight: 800; font-size: 16px;
      background: var(--pill-blue-bg); color: var(--pill-blue); }
    .vno.draft { background: var(--pill-amber-bg); color: var(--pill-amber); }
    .vno.active { background: var(--pill-green-bg); color: var(--pill-green); }
    .title { font: inherit; font-size: 19px; font-weight: 700; width: 100%; max-width: 620px; padding: 4px 8px; margin-left: -8px;
      border: 1px solid transparent; border-radius: 7px; background: transparent; color: var(--text); }
    .title:hover { border-color: var(--border); }
    .title:focus { outline: 2px solid var(--accent-soft); border-color: var(--accent); background: var(--surface); }
    .title-ro { font-size: 19px; margin: 4px 0 0; }
    .badges { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; margin-top: 4px; }
    .topn { display: inline-flex; align-items: center; gap: 5px; color: var(--text-2); }
    .topn input { width: 52px; padding: 5px 6px; border: 1px solid var(--border); border-radius: 7px; background: var(--surface); }
    .changes { list-style: none; padding: 0; margin: 0; display: grid; gap: 8px; }
    .changes li { padding: 8px 10px; border: 1px solid var(--border); border-radius: 8px; font-size: 13px; color: var(--text); }
    @media (max-width: 640px) { .head-top { flex-wrap: wrap; } .vno { width: 40px; height: 40px; } }
  `],
})
export class MethodologyEditorPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly store = inject(MethodologyStore);

  readonly id = input.required({ transform: numberAttribute });
  /** ?edit=<criterion key> opens that criterion's editor; ?add=1 opens "Add new criterion" (?add=custom: with a custom field). */
  readonly edit = input<string | undefined>();
  readonly add = input<string | undefined>();

  protected readonly detail = signal<VersionDetail | null>(null);
  protected readonly fields = signal<ScoringFieldInfo[]>([]);
  protected readonly rules = signal<MethodologyRules | null>(null);
  protected readonly name = signal('');
  protected readonly summary = signal('');
  protected readonly validation = signal<ValidationResult | null>(null);
  protected readonly preview = signal<PreviewSummary | null>(null);
  protected readonly busy = signal<false | 'save' | 'preview' | 'other'>(false);
  protected readonly dialog = signal(false);
  protected readonly topN = signal(10);
  protected readonly message = signal<{ text: string; error: boolean } | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly editing = signal<{ mode: 'edit' | 'add'; criterion: Criterion | null; index: number; custom: boolean } | null>(null);

  protected readonly editable = computed(() => this.detail()?.version.status === 'Draft' && !this.detail()?.version.isBaseline);
  protected readonly keys = computed(() => (this.rules()?.criteria ?? []).map(c => c.key));
  protected readonly dirty = computed(() => {
    const d = this.detail();
    if (!d || !this.editable()) return false;
    return JSON.stringify(this.rules()) !== JSON.stringify(d.rules) || this.name() !== d.version.name || (this.summary() || null) !== (d.version.changeSummary ?? null);
  });
  protected readonly errorCount = computed(() => (this.validation()?.issues ?? []).filter(i => i.severity === 'Error').length);
  protected readonly valid = computed(() => this.validation()?.valid ?? true);
  protected readonly enabledCount = computed(() => (this.rules()?.criteria ?? []).filter(c => c.enabled).length);
  protected readonly globalIssues = computed(() => (this.validation()?.issues ?? []).filter(i => !i.path.startsWith('criteria[')));
  /** A preview is only current if it used these rules AND was compared with the version that is active right now. */
  protected readonly previewShown = computed(() => {
    const p = this.preview(), d = this.detail();
    return !!p && !!d && p.rulesHash === d.version.rulesHash && p.comparedToVersionId === this.store.versionId();
  });
  protected readonly sitesLabel = computed(() => (this.fields()[0] ? (this.fields()[0].present + this.fields()[0].missing).toLocaleString() : 'all'));
  protected readonly publishable = computed(() => {
    const d = this.detail(), p = this.preview();
    return !!d && !!p && !this.dirty() && !this.busy() && !d.version.isActive && d.version.previewCurrent && this.previewShown();
  });

  protected readonly cards = computed(() => (this.rules()?.criteria ?? []).map(c => cardItem(c, this.fields())));
  protected readonly changedKeys = computed(() => (this.rules()?.criteria ?? []).filter(c => this.isChanged(c)).map(c => c.key));
  protected readonly issuesByKey = computed(() => {
    const out: Record<string, ValidationIssue[]> = {};
    const issues = this.validation()?.issues ?? [];
    (this.rules()?.criteria ?? []).forEach((c, i) => {
      const prefix = `criteria[${i}]`;
      const own = issues.filter(x => x.path === prefix || x.path.startsWith(prefix + '.'));
      if (own.length) out[c.key] = own;
    });
    return out;
  });
  protected readonly steps = computed(() => {
    const d = this.detail();
    const changed = this.dirty() || (d?.changesVsActive.length ?? 0) > 0;
    const saved = !this.dirty() && (d?.changesVsActive.length ?? 0) > 0;
    const previewed = this.previewShown() && !this.dirty();
    type S = 'done' | 'current' | 'todo';
    const st = (done: boolean, current: boolean): S => (done ? 'done' : current ? 'current' : 'todo');
    return [
      { n: 1, title: 'Edit rules', note: changed ? 'Changes made' : 'Edit or add rules', state: st(changed, !changed) },
      { n: 2, title: 'Save draft', note: this.dirty() ? 'Unsaved changes' : saved ? 'Saved' : 'Nothing to save yet', state: st(saved, this.dirty()) },
      { n: 3, title: `Review Results (${this.sitesLabel()} sites)`, note: previewed ? 'Up to date' : 'Recalculate scores & ranks', state: st(previewed, saved && !previewed) },
      { n: 4, title: 'Apply Scoring', note: 'Typed confirmation', state: st(false, this.publishable()) },
    ];
  });

  private readonly validate$ = new Subject<MethodologyRules>();
  private pendingOpen: { edit?: string; add?: boolean; custom?: boolean } | null = null;

  constructor() {
    this.api.methodologyFields().subscribe(f => this.fields.set(f));
    effect(() => {
      const id = this.id();   // reload only when the version changes (query params just open the drawer once)
      untracked(() => {
        const add = this.add();
        this.pendingOpen = { edit: this.edit() || undefined, add: !!add, custom: add === 'custom' };
        this.load(id);
      });
    });
    // Live validation (schema check only — nothing is recalculated while editing).
    this.validate$.pipe(debounceTime(300), switchMap(r => this.api.validateRules(r).pipe(catchError(() => of(null)))))
      .subscribe(v => this.validation.set(v));
  }

  private load(id: number) {
    this.api.methodologyVersion(id).subscribe({
      next: d => {
        this.setDetail(d);
        const open = this.pendingOpen;
        this.pendingOpen = null;
        if (open?.add && this.editable()) this.openAdd(open.custom);
        else if (open?.edit && this.editable()) this.openEdit(open.edit);
        if (open?.add || open?.edit) this.router.navigate([], { queryParams: { edit: null, add: null }, queryParamsHandling: 'merge', replaceUrl: true });
      },
      error: (e: HttpErrorResponse) => this.loadError.set(e.status === 404 ? 'Version not found.' : problem(e)),
    });
  }

  private setDetail(d: VersionDetail) {
    this.detail.set(d);
    this.rules.set(structuredClone(d.rules));
    this.name.set(d.version.name);
    this.summary.set(d.version.changeSummary ?? '');
    this.preview.set(d.preview);
    this.validation.set({ valid: !d.issues.some(i => i.severity === 'Error'), issues: d.issues, minPossible: d.minPossible, maxPossible: d.maxPossible });
  }

  protected reloadFields() { this.api.methodologyFields().subscribe(f => this.fields.set(f)); }

  private isChanged(c: Criterion) {
    const saved = this.detail()?.rules.criteria.find(x => x.key === c.key);
    return this.editable() && JSON.stringify(saved) !== JSON.stringify(c);
  }

  /** Edit: in a draft, opens the drawer; on a published version, opens (or creates) a working draft based on it. */
  protected openEdit(key: string) {
    const r = this.rules();
    if (!r) return;
    if (!this.editable()) { this.goToDraft({ edit: key }); return; }
    const index = r.criteria.findIndex(c => c.key === key);
    if (index >= 0) this.editing.set({ mode: 'edit', criterion: r.criteria[index], index, custom: false });
  }

  protected openAdd(custom = false) {
    if (!this.editable()) { this.goToDraft({ add: custom ? 'custom' : '1' }); return; }
    this.editing.set({ mode: 'add', criterion: null, index: -1, custom });
  }

  private goToDraft(query: Record<string, string>) {
    const d = this.detail();
    if (!d) return;
    this.busy.set('other');
    this.store.workingDraft(d.version.id).subscribe({
      next: id => { this.busy.set(false); this.router.navigate(['/methodology/versions', id], { queryParams: query }); },
      error: (e: HttpErrorResponse) => { this.busy.set(false); this.message.set({ text: problem(e), error: true }); },
    });
  }

  protected applyDialog(c: Criterion) {
    const e = this.editing(), r = this.rules();
    if (!e || !r) return;
    this.setRules({ ...r, criteria: e.mode === 'add' ? [...r.criteria, c] : r.criteria.map((x, k) => (k === e.index ? c : x)) });
    this.editing.set(null);
    this.message.set({ text: `${e.mode === 'add' ? 'Added' : 'Updated'} “${c.name}” in the draft — save the draft, then preview.`, error: false });
  }

  protected toggle(key: string) {
    const r = this.rules();
    if (r) this.setRules({ ...r, criteria: r.criteria.map(x => (x.key === key ? { ...x, enabled: !x.enabled } : x)) });
  }

  protected removeCriterion(key: string) {
    const r = this.rules();
    const c = r?.criteria.find(x => x.key === key);
    if (!r || !c || c.builtIn) return;
    if (!confirm(`Remove “${c.name}” from this draft? (Original criteria can only be turned off.)`)) return;
    this.setRules({ ...r, criteria: r.criteria.filter(x => x.key !== key) });
  }

  private setRules(r: MethodologyRules) {
    this.rules.set(r);
    this.validate$.next(r);
  }

  protected save() {
    const d = this.detail(), r = this.rules();
    if (!d || !r) return;
    this.busy.set('save');
    this.api.saveDraft(d.version.id, this.name().trim(), this.summary().trim() || null, r).subscribe({
      next: nd => { this.busy.set(false); this.setDetail(nd); this.message.set({ text: 'Draft saved. Run the preview to see the effect on every site.', error: false }); },
      error: (e: HttpErrorResponse) => { this.busy.set(false); this.message.set({ text: problem(e), error: true }); },
    });
  }

  protected runPreview() {
    const d = this.detail();
    if (!d) return;
    this.busy.set('preview');
    this.message.set(null);
    this.api.previewVersion(d.version.id, this.topN()).subscribe({
      next: p => {
        this.busy.set(false);
        this.preview.set(p);
        this.api.methodologyVersion(d.version.id).subscribe(nd => this.setDetail({ ...nd, preview: p }));
      },
      error: (e: HttpErrorResponse) => { this.busy.set(false); this.message.set({ text: problem(e), error: true }); },
    });
  }

  protected onPublished(r: PublishResult) {
    this.dialog.set(false);
    this.store.refresh();   // bumps the epoch → every view reloads with the new scores
    this.message.set({ text: `v${r.activeVersionId} is now active — ${r.sitesUpdated.toLocaleString()} sites re-scored and re-ranked in ${r.elapsedMs} ms.`, error: false });
    this.load(r.activeVersionId);
  }

  protected deleteDraft() {
    const d = this.detail();
    if (!d || !confirm(`Delete draft “${d.version.name}”? This cannot be undone.`)) return;
    this.api.deleteDraft(d.version.id).subscribe({
      next: () => this.router.navigateByUrl('/methodology'),
      error: (e: HttpErrorResponse) => this.message.set({ text: problem(e), error: true }),
    });
  }

  protected val(e: Event) { return (e.target as HTMLInputElement).value; }
}
