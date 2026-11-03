import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SavedSite } from '../../core/models';
import { SavedStore } from '../../core/saved-store';
import { scoreCss } from '../../shared/score-color';
import { CompareToggleComponent } from '../compare/compare-toggle.component';

/** One bookmarked site: its candidate summary and an optional note. */
@Component({
  selector: 'app-saved-card',
  imports: [DecimalPipe, DatePipe, FormsModule, RouterLink, CompareToggleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (saved(); as s) {
      <article class="panel saved-card" [class.lost]="!s.candidateFound">
        <div class="sc-head">
          <div class="sc-title">
            <h3>{{ s.address || s.siteId }}</h3>
            <div class="muted small">
              @if (s.candidateFound) {
                {{ s.municipality }}, {{ s.county }} County · seg {{ s.segment }} · rank #{{ s.rank }} · {{ s.siteId }}
              } @else {
                {{ s.siteId }} · <span class="neg-text">no longer in the current dataset</span>
              }
            </div>
          </div>
          @if (s.candidateFound && s.score != null) {
            <span class="score-pill"><i [style.background]="color(s.score)"></i>{{ s.score | number: '1.1-1' }}</span>
          }
        </div>

        @if (s.candidateFound) {
          <div class="sc-facts small">
            <span>{{ s.propClass }}</span>
            <span>{{ s.openAcres | number: '1.1-1' }} open ac</span>
            <span>{{ s.milesToExit | number: '1.2-2' }} mi to {{ s.nearestHighway || 'exit' }}</span>
          </div>
        }

        <label class="field">
          <span>Note</span>
          <textarea rows="2" [(ngModel)]="notes" placeholder="Why this site is worth evaluating…"></textarea>
        </label>

        <div class="sc-foot">
          <div class="muted small">Saved {{ s.createdAtUtc | date: 'MMM d, y' }}</div>
          <div class="sc-actions">
            <a class="btn-ghost small" [routerLink]="['/saved', s.id]">Open →</a>
            @if (s.candidateFound) {
              <app-compare-toggle [item]="{ siteId: s.siteId, label: s.address || s.siteId, segment: s.segment }" [compact]="true" />
            }
            @if (s.candidateFound && s.lat != null) {
              <a class="btn-ghost small" [href]="mapsUrl()" target="_blank" rel="noopener">Map ↗</a>
            }
            <button type="button" class="btn-ghost small" [disabled]="!dirty()" (click)="apply()">Save note</button>
            <button type="button" class="btn-ghost small danger" [disabled]="store.isBusy(s.siteId)" (click)="remove()">Remove</button>
          </div>
        </div>
      </article>
    }
  `,
})
export class SavedCardComponent {
  protected readonly store = inject(SavedStore);
  protected readonly color = scoreCss;

  readonly saved = input.required<SavedSite>();

  protected readonly notes = linkedSignal(() => this.saved().notes ?? '');
  protected readonly dirty = computed(() => this.notes() !== (this.saved().notes ?? ''));

  protected mapsUrl() {
    const s = this.saved();
    return `https://www.google.com/maps/search/?api=1&query=${s.lat},${s.lng}`;
  }

  protected apply() {
    this.store.update(this.saved().id, { notes: this.notes().trim() || null });
  }

  protected remove() {
    const s = this.saved();
    this.store.unsave(s.id, s.siteId);
  }
}
