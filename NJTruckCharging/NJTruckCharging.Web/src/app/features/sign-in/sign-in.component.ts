import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { AuthService } from '../../core/auth.service';

/** One simple step: work e-mail and password (accounts are created by the administrator). Large text, clear errors. */
@Component({
  selector: 'app-sign-in',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="si-wrap">
      <section class="si-card" aria-labelledby="si-title">
        <div class="si-brand"><span aria-hidden="true">⚡</span> C3 Site Finder</div>

        @if (auth.state() === 'loading') {
          <h1 id="si-title">Loading…</h1>
        } @else if (auth.state() === 'denied') {
          <h1 id="si-title">Your account is not approved yet</h1>
          <p>You signed in as <b>{{ auth.email() }}</b>, but this account does not have access to C3 Site Finder.
            Please ask the administrator to approve it.</p>
          <button type="button" class="btn primary si-btn" (click)="auth.signOut()">Sign out</button>
        } @else {
          <h1 id="si-title">Sign in</h1>
          <p>Enter your work e-mail address and the password the administrator gave you.</p>
          @if (auth.notice(); as n) { <p class="si-note" role="status">{{ n }}</p> }
          <form (submit)="signIn($event)" novalidate>
            <label for="si-email">Work e-mail</label>
            <input #emailBox id="si-email" type="email" name="username" autocomplete="username" inputmode="email" required
                   spellcheck="false" [value]="email()" (input)="email.set(val($event))"
                   [attr.aria-invalid]="error() ? 'true' : null" [attr.aria-describedby]="error() ? 'si-error' : null" />

            <label for="si-password">Password</label>
            <div class="si-pw">
              <input #passwordBox id="si-password" [type]="showPassword() ? 'text' : 'password'" name="password"
                     autocomplete="current-password" required spellcheck="false" autocapitalize="off"
                     [value]="password()" (input)="password.set(val($event))"
                     [attr.aria-invalid]="error() ? 'true' : null" [attr.aria-describedby]="error() ? 'si-error' : null" />
              <button type="button" class="btn si-show" (click)="showPassword.set(!showPassword())"
                      [attr.aria-pressed]="showPassword()" aria-controls="si-password">
                {{ showPassword() ? 'Hide' : 'Show' }}<span class="si-sr"> password</span>
              </button>
            </div>

            @if (error(); as e) { <p id="si-error" class="si-error" role="alert">{{ e }}</p> }
            <button type="submit" class="btn primary si-btn" [disabled]="busy() || !email().trim() || !password()">
              {{ busy() ? 'Signing in…' : 'Sign in' }}
            </button>
          </form>
          <p class="si-small">Only employees whose account the administrator created can sign in.
            Forgot your password? Please ask the administrator.</p>
        }
      </section>
    </main>
  `,
  styles: [`
    .si-wrap { min-height: 100vh; display: grid; place-items: center; padding: 24px 16px; background: var(--bg); box-sizing: border-box; }
    .si-card { width: 100%; max-width: 460px; box-sizing: border-box; background: var(--surface); color: var(--text);
      border: 1px solid var(--border); border-radius: 14px; padding: 32px 28px; box-shadow: 0 8px 30px rgba(15, 23, 42, .08); }
    .si-brand { font-weight: 700; font-size: 16px; color: var(--text-2); margin-bottom: 18px; }
    h1 { font-size: 26px; line-height: 1.25; margin: 0 0 12px; }
    p { font-size: 17px; line-height: 1.55; margin: 0 0 16px; }
    label { display: block; font-size: 17px; font-weight: 600; margin: 14px 0 8px; }
    input { width: 100%; box-sizing: border-box; min-height: 52px; font-size: 20px; padding: 10px 14px; border-radius: 10px;
      border: 2px solid var(--border); background: var(--surface); color: var(--text); }
    input:focus-visible { outline: 3px solid #2563eb; outline-offset: 2px; border-color: #2563eb; }
    .si-pw { display: flex; gap: 8px; align-items: stretch; }
    .si-pw input { flex: 1; min-width: 0; }
    .si-show { min-height: 52px; min-width: 76px; font-size: 16px; font-weight: 600; border-radius: 10px; padding: 0 12px; }
    .si-sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    .si-btn { width: 100%; min-height: 52px; font-size: 18px; font-weight: 600; margin-top: 18px; border-radius: 10px; }
    .si-error { color: #b91c1c; font-weight: 600; margin: 12px 0 0; font-size: 16px; }
    .si-note { background: var(--pill-amber-bg); color: var(--pill-amber); padding: 10px 12px; border-radius: 8px; font-size: 16px; }
    .si-small { font-size: 15px; color: var(--text-2); margin: 18px 0 0; }
    :host-context([data-theme="dark"]) .si-error { color: #fca5a5; }
  `],
})
export class SignInComponent {
  protected readonly auth = inject(AuthService);
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly showPassword = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly emailBox = viewChild<ElementRef<HTMLInputElement>>('emailBox');
  private readonly passwordBox = viewChild<ElementRef<HTMLInputElement>>('passwordBox');

  constructor() {
    // Put the cursor in the e-mail box as soon as it appears (after "Loading…"), once.
    let focused = false;
    effect(() => {
      const box = this.emailBox();
      if (box && !focused) { focused = true; box.nativeElement.focus(); }
    });
  }

  protected val(e: Event): string { return (e.target as HTMLInputElement).value; }

  protected async signIn(e: Event) {
    e.preventDefault();
    const email = this.email().trim().toLowerCase();
    const password = this.password();
    if (!email || !password || this.busy()) return;
    this.busy.set(true); this.error.set(null);
    const err = await this.auth.signIn(email, password);
    this.busy.set(false);
    if (err) {
      // Never keep a rejected password around; put the cursor back in the password box.
      this.password.set('');
      this.error.set(err);
      setTimeout(() => this.passwordBox()?.nativeElement.focus());
    }
  }
}
