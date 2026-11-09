import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { SavedStore } from './core/saved-store';
import { CompareStore } from './core/compare-store';
import { ThemeService } from './core/theme.service';
import { MethodologyStore } from './core/methodology-store';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark" aria-hidden="true">⚡</span>
        <div>
          <div class="brand-name">EV Edison <span>·</span> Power Edison</div>
          <div class="brand-sub">NJ C3 medium &amp; heavy-duty charging · site finder</div>
        </div>
      </div>
      <div class="topbar-right">
        <nav>
          <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Dashboard</a>
          <a routerLink="/saved" routerLinkActive="active">
            Saved
            @if (saved.totalSaved() > 0) { <span class="nav-badge">{{ saved.totalSaved() }}</span> }
          </a>
          <a routerLink="/screening" routerLinkActive="active">Screening</a>
          <a routerLink="/compare" routerLinkActive="active">
            Compare
            @if (compare.count() > 0) { <span class="nav-badge">{{ compare.count() }}</span> }
          </a>
          <a routerLink="/methodology" routerLinkActive="active">Methodology</a>
        </nav>
        @if (methodology.active(); as m) {
          <a class="method-chip" routerLink="/methodology" [title]="'Scores shown everywhere come from methodology ' + methodology.label()">
            Scoring v{{ m.version.id }}{{ m.version.isBaseline ? ' · original' : ' · custom' }}
          </a>
        }
        <button type="button" class="theme-toggle" (click)="theme.toggle()"
                [title]="theme.isDark() ? 'Switch to light mode' : 'Switch to dark mode'"
                [attr.aria-label]="theme.isDark() ? 'Switch to light mode' : 'Switch to dark mode'">
          @if (theme.isDark()) {
            <!-- sun: currently dark → offer light -->
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
            </svg>
          } @else {
            <!-- moon: currently light → offer dark -->
            <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />
            </svg>
          }
        </button>
      </div>
    </header>
    <main class="page"><router-outlet /></main>
    @if (compare.count() > 0 && !onComparePage()) {
      <aside class="compare-tray" aria-label="Comparison selection">
        <span class="ct-count"><b>{{ compare.count() }}</b> / 5 to compare</span>
        <span class="ct-items">
          @for (i of compare.items(); track i.siteId) {
            <span class="chip on" [title]="i.siteId">{{ i.label }}
              <button type="button" class="chip-x" (click)="compare.remove(i.siteId)" [attr.aria-label]="'Remove ' + i.label">✕</button>
            </span>
          }
        </span>
        @if (compare.notice(); as n) { <span class="ct-notice">{{ n }}</span> }
        <button type="button" class="btn-ghost small" (click)="compare.clear()">Clear</button>
        <a class="btn primary" routerLink="/compare" [class.disabled]="!compare.canCompare()"
           [title]="compare.canCompare() ? 'Open side-by-side comparison' : 'Select at least 2 sites'">Compare →</a>
      </aside>
    }
  `,
})
export class App {
  protected readonly saved = inject(SavedStore);
  protected readonly compare = inject(CompareStore);
  private readonly router = inject(Router);
  private readonly url = toSignal(this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(e => e.urlAfterRedirects)),
    { initialValue: this.router.url });
  protected readonly onComparePage = computed(() => (this.url() ?? '').startsWith('/compare'));
  protected readonly theme = inject(ThemeService);
  protected readonly methodology = inject(MethodologyStore);
}
