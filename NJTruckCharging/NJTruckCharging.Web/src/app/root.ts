import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { App } from './app';
import { AuthService } from './core/auth.service';
import { installAuthenticatedDownloads } from './core/authenticated-downloads';
import { ThemeService } from './core/theme.service';
import { SignInComponent } from './features/sign-in/sign-in.component';

/**
 * Sign-in gate. Until an approved user is signed in, nothing else is created — no store, page or API call — so no
 * C3 data is requested without a token. (The API refuses such requests anyway.) Without sign-in configured
 * (local development) the dashboard shows directly, as before.
 */
@Component({
  selector: 'app-root',
  imports: [App, SignInComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (auth.state() === 'signed-in') { <app-shell /> } @else { <app-sign-in /> }
  `,
})
export class Root {
  protected readonly auth = inject(AuthService);
  private readonly theme = inject(ThemeService);   // applies the saved Light/Dark theme to the sign-in screen too

  constructor() {
    inject(DestroyRef).onDestroy(installAuthenticatedDownloads(this.auth));
  }
}
