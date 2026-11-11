import { ChangeDetectionStrategy, Component, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ChangeAction, ChangeLogEntry, ChangeSnapshot, Criterion } from '../../core/methodology.models';

const ACTION: Record<ChangeAction, string> = {
  CreateDraft: 'Draft created', SaveDraft: 'Draft saved', DeleteDraft: 'Draft deleted',
  Preview: 'Results reviewed', Publish: 'Scoring applied', Restore: 'Version restored',
};

/**
 * The methodology audit trail: every change made through the dashboard with the editor's verified e-mail and Supabase
 * user id, the time, the revision and the previous → new values. Saves and deletions also keep full before/after
 * snapshots ("Show previous and new values"). Read-only; written by the API in the same transaction as the change.
 */
@Component({
  selector: 'app-change-log',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (entries().length) {
      <ol class="log" [attr.aria-label]="versionId() === null ? 'Methodology audit trail' : 'Change history of this version'">
        @for (e of entries(); track e.id) {
          <li>
            <div class="row">
              <span class="pill" [class.green]="e.action === 'Publish' || e.action === 'Restore'" [class.red]="e.action === 'DeleteDraft'"
                    [class.blue]="e.action === 'SaveDraft'">{{ label(e.action) }}</span>
              @if (versionId() === null) {
                @if (e.action === 'DeleteDraft') { <b>v{{ e.versionId }} · {{ e.versionName }}</b> }
                @else { <a [routerLink]="['/methodology/versions', e.versionId]"><b>v{{ e.versionId }} · {{ e.versionName }}</b></a> }
              }
              <span class="who" [title]="e.actorUserId ? 'Supabase user id ' + e.actorUserId : 'Local (trusted network) or system'">
                {{ e.actorEmail ?? 'local / system' }}@if (e.actorUserId) { <code>{{ e.actorUserId.slice(0, 8) }}…</code> }</span>
              <time class="when" [attr.datetime]="e.atUtc">{{ e.atUtc | date: 'medium' }}</time>
            </div>
            <div class="meta">
              @if (e.revisionBefore !== null && e.revisionAfter !== null && e.revisionBefore !== e.revisionAfter) { revision {{ e.revisionBefore }} → {{ e.revisionAfter }} · }
              @if (e.previousActiveVersionId !== null) { replaced v{{ e.previousActiveVersionId }} as the live method · }
              @if (e.rulesHashBefore && e.rulesHashAfter && e.rulesHashBefore !== e.rulesHashAfter) { rules {{ e.rulesHashBefore.slice(0, 8) }} → {{ e.rulesHashAfter.slice(0, 8) }} }
            </div>
            @if (e.note) { <p class="note">{{ e.note }}</p> }
            @if (e.changes.length) {
              <ul class="changes">
                @for (c of e.changes; track $index) {
                  <li><b>{{ c.name }}</b> <span class="mm-xs">({{ c.change }})</span>
                    @for (d of c.details; track $index) { <div class="detail">{{ d }}</div> }</li>
                }
              </ul>
            }
            @if (e.hasSnapshots) {
              <button type="button" class="btn-ghost small" (click)="toggle(e.id)" [attr.aria-expanded]="open() === e.id">
                {{ open() === e.id ? 'Hide previous and new values' : 'Show previous and new values' }}</button>
              @if (open() === e.id) {
                @if (snapshot(); as s) {
                  <div class="snap">
                    @for (d of diff(s); track d.key) {
                      <div class="pair">
                        <div><span class="mm-xs">Previous</span><pre>{{ d.before }}</pre></div>
                        <div><span class="mm-xs">New</span><pre>{{ d.after }}</pre></div>
                      </div>
                    } @empty { <p class="mm-xs">No rule values differ (name or summary only).</p> }
                  </div>
                } @else { <p class="mm-xs">Loading…</p> }
              }
            }
          </li>
        }
      </ol>
      @if (total() > entries().length) {
        <button type="button" class="btn small" (click)="more()">Show older changes ({{ total() - entries().length }} more)</button>
      }
    } @else if (loaded()) {
      <p class="mm-xs mm-muted">No changes recorded yet.</p>
    }
  `,
  styles: [`
    .log { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
    .log > li { border: 1px solid var(--border); border-radius: 10px; padding: 10px 12px; display: grid; gap: 6px; font-size: 13px; color: var(--text); }
    .row { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
    .who { color: var(--text); font-weight: 600; }
    .who code { font-weight: 400; color: var(--text-3); margin-left: 4px; font-size: 11.5px; }
    .when, .meta { color: var(--text-3); font-size: 12px; }
    .note { margin: 0; color: var(--text-2); }
    .changes { margin: 0; padding-left: 18px; display: grid; gap: 4px; }
    .detail { color: var(--text-2); font-size: 12.5px; }
    .snap { display: grid; gap: 10px; }
    .pair { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .pair pre { margin: 2px 0 0; padding: 8px; background: var(--surface-2); border: 1px solid var(--border); border-radius: 6px;
      font-size: 11.5px; white-space: pre-wrap; word-break: break-word; max-height: 260px; overflow: auto; }
    @media (max-width: 640px) { .pair { grid-template-columns: 1fr; } }
  `],
})
export class ChangeLogComponent {
  private readonly api = inject(ApiService);

  /** null = the whole team's audit trail; a number = one version's history. */
  readonly versionId = input<number | null>(null);
  /** Change it to reload (e.g. after a save). */
  readonly refresh = input(0);

  protected readonly entries = signal<ChangeLogEntry[]>([]);
  protected readonly total = signal(0);
  protected readonly loaded = signal(false);
  protected readonly open = signal<number | null>(null);
  protected readonly snapshot = signal<ChangeSnapshot | null>(null);
  private page = 1;

  constructor() {
    effect(() => {
      const id = this.versionId();
      this.refresh();
      untracked(() => { this.page = 1; this.load(id, true); });
    });
  }

  protected label(a: ChangeAction) { return ACTION[a] ?? a; }

  protected more() { this.page++; this.load(this.versionId(), false); }

  private load(id: number | null, reset: boolean) {
    this.api.methodologyChanges(id, this.page, 25).subscribe({
      next: p => {
        this.entries.set(reset ? p.entries : [...this.entries(), ...p.entries]);
        this.total.set(p.total);
        this.loaded.set(true);
      },
      error: () => this.loaded.set(true),
    });
  }

  protected toggle(id: number) {
    if (this.open() === id) { this.open.set(null); return; }
    this.open.set(id);
    this.snapshot.set(null);
    this.api.changeSnapshot(id).subscribe({ next: s => this.snapshot.set(s), error: () => this.open.set(null) });
  }

  /** Criteria whose stored values differ, as readable JSON (previous vs new); deletions show the deleted rules. */
  protected diff(s: ChangeSnapshot) {
    const before = new Map((s.before?.rules.criteria ?? []).map(c => [c.key, c]));
    const after = new Map((s.after?.rules.criteria ?? []).map(c => [c.key, c]));
    const keys = [...new Set([...before.keys(), ...after.keys()])];
    const show = (c: Criterion | undefined) => (c ? JSON.stringify(c, null, 2) : '(none)');
    return keys
      .filter(k => !s.after || JSON.stringify(before.get(k)) !== JSON.stringify(after.get(k)))
      .map(k => ({ key: k, before: show(before.get(k)), after: s.after ? show(after.get(k)) : '(draft deleted)' }));
  }
}
