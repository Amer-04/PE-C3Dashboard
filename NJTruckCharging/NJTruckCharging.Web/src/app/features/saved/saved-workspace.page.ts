import { ChangeDetectionStrategy, Component, computed, effect, inject, input, linkedSignal, numberAttribute, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { SavedSite } from '../../core/models';
import { SavedStore } from '../../core/saved-store';
import { scoreCss } from '../../shared/score-color';
import { ContactsPanelComponent } from '../contacts/contacts-panel.component';
import { FeasibilityPanelComponent } from '../feasibility/feasibility-panel.component';

/**
 * Evaluation workspace for a bookmarked candidate: geography/scoring summary, a working note, contact
 * research, and the feasibility checklist. A site-evaluation view — no sales pipeline or CRM.
 */
@Component({
  selector: 'app-saved-workspace-page',
  imports: [DecimalPipe, DatePipe, FormsModule, RouterLink, ContactsPanelComponent, FeasibilityPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/saved" class="btn-ghost small back-link">← Back to saved sites</a>

    @if (saved(); as s) {
      <section class="panel ws-header">
        <div>
          <div class="eyebrow">
            @if (s.candidateFound) { Segment {{ s.segment }} · rank #{{ s.rank }} } @else { Candidate not in current dataset }
            · {{ s.siteId }}
          </div>
          <h2>{{ s.address || s.siteId }}</h2>
          <div class="muted">@if (s.candidateFound) { {{ s.municipality }}, {{ s.county }} County }</div>
        </div>
        <div class="ws-score">
          @if (s.score != null) { <span class="score-pill"><i [style.background]="color(s.score)"></i>{{ s.score | number: '1.1-1' }}</span> }
        </div>
      </section>

      @if (store.error(); as e) { <div class="banner error" role="alert">{{ e }}</div> }

      <div class="ws-grid">
        <section class="panel">
          <h3>Geography & scoring</h3>
          @if (s.candidateFound) {
            <dl class="facts">
              <dt>Score</dt><dd>{{ s.score | number: '1.1-1' }} · rank #{{ s.rank }} in segment {{ s.segment }}</dd>
              <dt>Class</dt><dd>{{ s.propClass }}</dd>
              <dt>Open land</dt><dd>{{ s.openAcres | number: '1.1-1' }} ac</dd>
              <dt>Exit</dt><dd>{{ s.milesToExit | number: '1.2-2' }} mi · {{ s.nearestHighway || '—' }}</dd>
              <dt>Owner mail</dt><dd>{{ s.ownerMailAddress || '—' }}{{ s.ownerMailCity ? ', ' + s.ownerMailCity : '' }} {{ s.ownerMailZip }}</dd>
              @if (s.businessPhone) { <dt>Business</dt><dd><a [href]="'tel:' + s.businessPhone">{{ s.businessPhone }}</a></dd> }
            </dl>
            @if (s.lat != null) { <a class="btn" [href]="mapsUrl(s)" target="_blank" rel="noopener">Google Maps ↗</a> }
          } @else {
            <p class="muted">This bookmark's candidate record isn't in the current dataset (e.g. dropped by a re-import). The note, contacts and feasibility still work.</p>
          }
        </section>

        <section class="panel">
          <h3>Note</h3>
          <label class="field">
            <textarea rows="5" [(ngModel)]="notes" placeholder="Why this site is worth evaluating…"></textarea>
          </label>
          <div class="ws-foot">
            <span class="muted small">Saved {{ s.createdAtUtc | date: 'MMM d, y' }} · updated {{ s.updatedAtUtc | date: 'MMM d, y, h:mm a' }}</span>
            <button type="button" class="btn primary small" [disabled]="!dirty() || busy()" (click)="save()">Save note</button>
          </div>
        </section>

        <section class="panel ws-wide"><app-contacts-panel [siteId]="s.siteId" /></section>
        <section class="panel ws-wide"><app-feasibility-panel [siteId]="s.siteId" /></section>
      </div>
    } @else if (notFound()) {
      <section class="panel empty-state"><p class="muted">Saved site not found. It may have been removed. <a routerLink="/saved">Back to saved sites</a>.</p></section>
    }
  `,
})
export class SavedWorkspacePage {
  private readonly api = inject(ApiService);
  protected readonly store = inject(SavedStore);
  protected readonly color = scoreCss;

  readonly id = input(0, { transform: numberAttribute });

  protected readonly saved = signal<SavedSite | null>(null);
  protected readonly notFound = signal(false);
  protected readonly busy = signal(false);

  protected readonly notes = linkedSignal(() => this.saved()?.notes ?? '');
  protected readonly dirty = computed(() => {
    const s = this.saved();
    return !!s && this.notes() !== (s.notes ?? '');
  });

  constructor() {
    effect(() => { const id = this.id(); if (id) this.load(id); });
  }

  private load(id: number) {
    this.api.savedSite(id).subscribe({
      next: s => { this.saved.set(s); this.notFound.set(false); },
      error: () => { this.saved.set(null); this.notFound.set(true); },
    });
  }

  protected mapsUrl(s: SavedSite) { return `https://www.google.com/maps/search/?api=1&query=${s.lat},${s.lng}`; }

  protected save() {
    const s = this.saved();
    if (!s || this.busy()) return;
    this.busy.set(true);
    this.store.error.set(null);
    this.api.updateSaved(s.id, { notes: this.notes().trim() || null }).subscribe({
      next: updated => { this.busy.set(false); this.saved.set(updated); this.store.loadRefs(); },
      error: () => this.busy.set(false),
    });
  }
}
