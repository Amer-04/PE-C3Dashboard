import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { AuthService } from '../../core/auth.service';

/** Two simple steps: enter your work e-mail, then enter the code from the e-mail. Large text, one task per screen. */
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
        } @else if (step() === 'email') {
          <h1 id="si-title">Sign in</h1>
          <p>Enter your work e-mail address. We will e-mail you a sign-in code.</p>
          @if (auth.notice(); as n) { <p class="si-note" role="status">{{ n }}</p> }
          <form (submit)="send($event)">
            <label for="si-email">Work e-mail</label>
            <input #emailBox id="si-email" type="email" autocomplete="email" inputmode="email" required spellcheck="false"
                   [value]="email()" (input)="email.set(val($event))" [attr.aria-describedby]="error() ? 'si-error' : null" />
            @if (error(); as e) { <p id="si-error" class="si-error" role="alert">{{ e }}</p> }
            <button type="submit" class="btn primary si-btn" [disabled]="busy() || !email().trim()">
              {{ busy() ? 'Sending…' : 'Send me a code' }}
            </button>
          </form>
          <p class="si-small">Only people invited by the administrator can sign in.</p>
        } @else {
          <h1 id="si-title">Enter your code</h1>
          <p>We sent a sign-in code to <b>{{ email() }}</b>. It may take a minute to arrive. If you do not see it, check your junk folder.</p>
          <form (submit)="verify($event)">
            <label for="si-code">Sign-in code</label>
            <input #codeBox id="si-code" type="text" inputmode="numeric" autocomplete="one-time-code" maxlength="10" required
                   spellcheck="false" [value]="code()" (input)="code.set(val($event))" [attr.aria-describedby]="error() ? 'si-error' : null" />
            @if (error(); as e) { <p id="si-error" class="si-error" role="alert">{{ e }}</p> }
            <button type="submit" class="btn primary si-btn" [disabled]="busy() || digits().length < 6">
              {{ busy() ? 'Checking…' : 'Sign in' }}
            </button>
          </form>
          <button type="button" class="btn si-btn si-secondary" (click)="back()" [disabled]="busy()">Use a different e-mail or send a new code</button>
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
    label { display: block; font-size: 17px; font-weight: 600; margin: 8px 0; }
    input { width: 100%; box-sizing: border-box; min-height: 52px; font-size: 20px; padding: 10px 14px; border-radius: 10px;
      border: 2px solid var(--border); background: var(--surface); color: var(--text); }
    input:focus-visible { outline: 3px solid #2563eb; outline-offset: 2px; border-color: #2563eb; }
    #si-code { letter-spacing: .2em; font-variant-numeric: tabular-nums; }
    .si-btn { width: 100%; min-height: 52px; font-size: 18px; font-weight: 600; margin-top: 16px; border-radius: 10px; }
    .si-secondary { font-size: 16px; }
    .si-error { color: #b91c1c; font-weight: 600; margin: 10px 0 0; font-size: 16px; }
    .si-note { background: var(--pill-amber-bg); color: var(--pill-amber); padding: 10px 12px; border-radius: 8px; font-size: 16px; }
    .si-small { font-size: 15px; color: var(--text-2); margin: 18px 0 0; }
    :host-context([data-theme="dark"]) .si-error { color: #fca5a5; }
  `],
})
export class SignInComponent {
  protected readonly auth = inject(AuthService);
  protected readonly step = signal<'email' | 'code'>('email');
  protected readonly email = signal('');
  protected readonly code = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly emailBox = viewChild<ElementRef<HTMLInputElement>>('emailBox');
  private readonly codeBox = viewChild<ElementRef<HTMLInputElement>>('codeBox');

  constructor() {
    // Put the cursor in the e-mail box as soon as it appears (after "Loading…"), once.
    let focused = false;
    effect(() => {
      const box = this.emailBox();
      if (box && !focused) { focused = true; box.nativeElement.focus(); }
    });
  }

  protected digits(): string { return this.code().replace(/\s+/g, ''); }
  protected val(e: Event): string { return (e.target as HTMLInputElement).value; }

  protected async send(e: Event) {
    e.preventDefault();
    const email = this.email().trim().toLowerCase();
    if (!email || this.busy()) return;
    this.busy.set(true); this.error.set(null);
    const err = await this.auth.sendCode(email);
    this.busy.set(false);
    if (err) { this.error.set(err); return; }
    this.email.set(email);
    this.code.set('');
    this.step.set('code');
    setTimeout(() => this.codeBox()?.nativeElement.focus());
  }

  protected async verify(e: Event) {
    e.preventDefault();
    const code = this.digits();
    if (code.length < 6 || this.busy()) return;
    this.busy.set(true); this.error.set(null);
    const err = await this.auth.verifyCode(this.email(), code);
    this.busy.set(false);
    if (err) { this.error.set(err); setTimeout(() => this.codeBox()?.nativeElement.select()); }
  }

  protected back() {
    this.step.set('email'); this.error.set(null); this.code.set('');
    setTimeout(() => this.emailBox()?.nativeElement.focus());
  }
}
