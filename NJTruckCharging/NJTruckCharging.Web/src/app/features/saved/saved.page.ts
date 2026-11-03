import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { SavedStore } from '../../core/saved-store';
import { SavedCardComponent } from './saved-card.component';

/** Bookmarked candidate sites — sites flagged during discovery for closer evaluation. */
@Component({
  selector: 'app-saved-page',
  imports: [DecimalPipe, RouterLink, SavedCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel">
      <div class="panel-head">
        <div>
          <h2>Saved sites</h2>
          <div class="muted small">{{ store.totalSaved() | number }} bookmarked for closer evaluation</div>
        </div>
        <div class="head-actions">
          <a class="btn" routerLink="/">← Find more sites</a>
          <button type="button" class="btn" (click)="reload()">Refresh</button>
        </div>
      </div>
      @if (store.error(); as e) { <div class="banner error" role="alert">{{ e }}</div> }
    </section>

    @if (store.saved().length) {
      <div class="saved-list">
        @for (s of store.saved(); track s.id) { <app-saved-card [saved]="s" /> }
      </div>
    } @else {
      <section class="panel empty-state">
        <p><b>No saved sites yet.</b></p>
        <p class="muted">Open the <a routerLink="/">dashboard</a>, review candidates, and press <b>☆ Save</b> to bookmark the ones worth evaluating. They'll appear here.</p>
      </section>
    }
  `,
})
export class SavedPage {
  protected readonly store = inject(SavedStore);

  constructor() { this.reload(); }

  protected reload() {
    this.store.loadSaved();
    this.store.loadRefs();
  }
}
