import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { AuthService, CHECK_EMAIL_MESSAGE, WORK_EMAIL_MESSAGE } from '../../core/auth.service';
import { PASSWORD_MIN, isEmployeeEmail, passwordProblems } from '../../core/employee-email';

type Mode = 'sign-in' | 'create' | 'check-email';

/**
 * Sign in, or create an account with a Power Edison / EV Edison work e-mail address. Large text, clear errors. The
 * domain and password checks here only help the user; Supabase and the API enforce them.
 */
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
          <h1 id="si-title">This account does not have access</h1>
          <p>You signed in as <b>{{ auth.email() }}</b>, but this account does not have access to C3 Site Finder.
            Sign in with your Power Edison or EV Edison work account, or ask the administrator.</p>
          <button type="button" class="btn primary si-btn" (click)="auth.signOut()">Sign out</button>
        } @else if (mode() === 'check-email') {
          <h1 id="si-title">Check your e-mail</h1>
          <p role="status" class="si-ok">{{ info() }}</p>
          <p class="si-small">The link works once and expires. Nothing arrived after a few minutes? Check spam, then
            send it again.</p>
          <button type="button" class="btn si-btn si-secondary" (click)="resend()" [disabled]="busy()">
            {{ busy() ? 'Sending…' : 'Send the link again' }}
          </button>
          <button type="button" class="btn primary si-btn" (click)="go('sign-in')">Back to sign in</button>
        } @else {
          @if (selfRegistration) {
          <div class="si-tabs" role="tablist" aria-label="Sign in or create an account">
            <button type="button" role="tab" id="si-tab-in" [attr.aria-selected]="mode() === 'sign-in'"
                    aria-controls="si-panel" (click)="go('sign-in')">Sign in</button>
            <button type="button" role="tab" id="si-tab-create" [attr.aria-selected]="mode() === 'create'"
                    aria-controls="si-panel" (click)="go('create')">Create account</button>
          </div>
          }

          <div id="si-panel" [attr.role]="selfRegistration ? 'tabpanel' : null"
               [attr.aria-labelledby]="selfRegistration ? (mode() === 'create' ? 'si-tab-create' : 'si-tab-in') : null">
          @if (mode() === 'sign-in') {
            <h1 id="si-title">Sign in</h1>
            <p>Use your work e-mail address and your password.</p>
          } @else {
            <h1 id="si-title">Create account</h1>
            <p>For Power Edison and EV Edison employees. Use your work e-mail address
              (<b>…&#64;poweredison.com</b> or <b>…&#64;ev-edison.com</b>); we will send it a confirmation link.</p>
          }
          @if (auth.notice(); as n) { <p class="si-note" role="status">{{ n }}</p> }

          <form (submit)="submit($event)" novalidate>
            <label for="si-email">Work e-mail</label>
            <input #emailBox id="si-email" type="email" name="username" autocomplete="username" inputmode="email" required
                   spellcheck="false" autocapitalize="off" [value]="email()" (input)="email.set(val($event))"
                   [attr.aria-invalid]="emailInvalid() || error() ? 'true' : null"
                   [attr.aria-describedby]="describedBy('si-email-hint')" />
            @if (emailInvalid()) { <p id="si-email-hint" class="si-hint">{{ workEmailMessage }}</p> }

            <label for="si-password">{{ mode() === 'create' ? 'Choose a password' : 'Password' }}</label>
            <div class="si-pw">
              <input #passwordBox id="si-password" [type]="showPassword() ? 'text' : 'password'" name="password"
                     [attr.autocomplete]="mode() === 'create' ? 'new-password' : 'current-password'" required spellcheck="false"
                     autocapitalize="off" [value]="password()" (input)="password.set(val($event))"
                     [attr.aria-invalid]="error() ? 'true' : null" [attr.aria-describedby]="describedBy('si-pw-rules')" />
              <button type="button" class="btn si-show" (click)="showPassword.set(!showPassword())"
                      [attr.aria-pressed]="showPassword()" aria-controls="si-password">
                {{ showPassword() ? 'Hide' : 'Show' }}<span class="si-sr"> password</span>
              </button>
            </div>

            @if (mode() === 'create') {
              <p id="si-pw-rules" class="si-hint" [class.si-good]="!passwordIssues().length">
                At least {{ passwordMin }} characters, with a letter and a number.
                @if (password() && passwordIssues().length) { Still needed: {{ passwordIssues().join(', ') }}. }
              </p>
              <label for="si-confirm">Type the password again</label>
              <input id="si-confirm" [type]="showPassword() ? 'text' : 'password'" name="confirm" autocomplete="new-password"
                     required spellcheck="false" autocapitalize="off" [value]="confirm()" (input)="confirm.set(val($event))"
                     [attr.aria-invalid]="confirm() && confirm() !== password() ? 'true' : null" />
              @if (confirm() && confirm() !== password()) { <p class="si-hint">The two passwords are not the same.</p> }
            }

            @if (error(); as e) { <p id="si-error" class="si-error" role="alert">{{ e }}</p> }
            <button type="submit" class="btn primary si-btn" [disabled]="!canSubmit()">
              @if (mode() === 'create') { {{ busy() ? 'Creating account…' : 'Create account' }} }
              @else { {{ busy() ? 'Signing in…' : 'Sign in' }} }
            </button>
          </form>
          </div>

          @if (!selfRegistration) {
            <p class="si-small">Only employees with an account can sign in. Forgot your password? Please ask the administrator.</p>
          } @else if (mode() === 'sign-in') {
            <p class="si-small">No account yet? <button type="button" class="si-link" (click)="go('create')">Create one</button>
              with your work e-mail address.
              <button type="button" class="si-link" (click)="resendFromSignIn()" [disabled]="busy()">Send the confirmation link again</button>.
              Forgot your password? Please ask the administrator.</p>
          } @else {
            <p class="si-small">Already have an account? <button type="button" class="si-link" (click)="go('sign-in')">Sign in</button>.</p>
          }
        }
      </section>
    </main>
  `,
  styles: [`
    .si-wrap { min-height: 100vh; display: grid; place-items: center; padding: 24px 16px; background: var(--bg); box-sizing: border-box; }
    .si-card { width: 100%; max-width: 480px; box-sizing: border-box; background: var(--surface); color: var(--text);
      border: 1px solid var(--border); border-radius: 14px; padding: 32px 28px; box-shadow: 0 8px 30px rgba(15, 23, 42, .08); }
    .si-brand { font-weight: 700; font-size: 16px; color: var(--text-2); margin-bottom: 18px; }
    .si-tabs { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; margin-bottom: 20px; padding: 4px;
      background: var(--bg); border: 1px solid var(--border); border-radius: 12px; }
    .si-tabs button { min-height: 44px; font-size: 16px; font-weight: 600; border: 0; border-radius: 9px; cursor: pointer;
      background: transparent; color: var(--text-2); }
    .si-tabs button[aria-selected="true"] { background: var(--surface); color: var(--text); box-shadow: 0 1px 3px rgba(15, 23, 42, .15); }
    .si-tabs button:focus-visible, .si-link:focus-visible { outline: 3px solid #2563eb; outline-offset: 2px; }
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
    .si-secondary { background: var(--surface); border: 2px solid var(--border); color: var(--text); }
    .si-hint { font-size: 15px; color: var(--text-2); margin: 8px 0 0; }
    .si-good { color: #15803d; }
    .si-error { color: #b91c1c; font-weight: 600; margin: 12px 0 0; font-size: 16px; }
    .si-note { background: var(--pill-amber-bg); color: var(--pill-amber); padding: 10px 12px; border-radius: 8px; font-size: 16px; }
    .si-ok { background: var(--bg); border: 1px solid var(--border); padding: 12px 14px; border-radius: 8px; }
    .si-small { font-size: 15px; color: var(--text-2); margin: 18px 0 0; }
    .si-link { background: none; border: 0; padding: 0; font: inherit; color: #2563eb; text-decoration: underline; cursor: pointer; }
    .si-link:disabled { opacity: .6; cursor: default; }
    :host-context([data-theme="dark"]) .si-error { color: #fca5a5; }
    :host-context([data-theme="dark"]) .si-good { color: #86efac; }
    :host-context([data-theme="dark"]) .si-link { color: #93c5fd; }
  `],
})
export class SignInComponent {
  protected readonly auth = inject(AuthService);
  protected readonly mode = signal<Mode>('sign-in');
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly confirm = signal('');
  protected readonly showPassword = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal(CHECK_EMAIL_MESSAGE);
  protected readonly passwordMin = PASSWORD_MIN;
  protected readonly selfRegistration = this.auth.config.selfRegistration;
  protected readonly workEmailMessage = WORK_EMAIL_MESSAGE;
  private readonly emailBox = viewChild<ElementRef<HTMLInputElement>>('emailBox');
  private readonly passwordBox = viewChild<ElementRef<HTMLInputElement>>('passwordBox');

  private readonly normalizedEmail = computed(() => this.email().trim());
  /** Only on the Create account form, and only once something that looks like a full address was typed. */
  protected readonly emailInvalid = computed(() =>
    this.mode() === 'create' && this.normalizedEmail().includes('.') && this.normalizedEmail().includes('@')
    && !isEmployeeEmail(this.normalizedEmail()));
  protected readonly passwordIssues = computed(() => passwordProblems(this.password()));
  protected readonly canSubmit = computed(() => {
    if (this.busy() || !this.normalizedEmail() || !this.password()) return false;
    if (this.mode() === 'sign-in') return true;
    return isEmployeeEmail(this.normalizedEmail()) && !this.passwordIssues().length && this.confirm() === this.password();
  });

  constructor() {
    // Put the cursor in the e-mail box as soon as it appears (after "Loading…"), once.
    let focused = false;
    effect(() => {
      const box = this.emailBox();
      if (box && !focused) { focused = true; box.nativeElement.focus(); }
    });
  }

  protected val(e: Event): string { return (e.target as HTMLInputElement).value; }

  protected describedBy(hint: string): string | null {
    const ids = [this.error() ? 'si-error' : '', hint === 'si-email-hint' && this.emailInvalid() ? hint : '',
      hint === 'si-pw-rules' && this.mode() === 'create' ? hint : ''].filter(Boolean);
    return ids.length ? ids.join(' ') : null;
  }

  protected go(mode: Mode) {
    if (mode !== 'sign-in' && !this.selfRegistration) return;
    this.mode.set(mode);
    this.error.set(null);
    this.password.set('');
    this.confirm.set('');
    if (mode !== 'check-email') setTimeout(() => this.emailBox()?.nativeElement.focus());
  }

  protected async submit(e: Event) {
    e.preventDefault();
    if (!this.canSubmit()) return;
    const email = this.normalizedEmail();
    const password = this.password();
    this.busy.set(true); this.error.set(null);
    try {
      if (this.mode() === 'create') {
        const r = await this.auth.signUp(email, password);
        if (r.ok) {
          this.info.set(CHECK_EMAIL_MESSAGE);
          this.auth.notice.set(null);
          this.go('check-email');
        } else {
          this.fail(r.message);
        }
      } else {
        const err = await this.auth.signIn(email.toLowerCase(), password);
        if (err) this.fail(err);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async resend() {
    if (this.busy()) return;
    this.busy.set(true);
    try { this.info.set(await this.auth.resendConfirmation(this.normalizedEmail())); }
    finally { this.busy.set(false); }
  }

  /** "Send the confirmation link again" from the sign-in form: needs the e-mail address typed above. */
  protected async resendFromSignIn() {
    if (!this.normalizedEmail()) {
      this.error.set('Type your work e-mail address above first.');
      this.emailBox()?.nativeElement.focus();
      return;
    }
    await this.resend();
    this.mode.set('check-email');
  }

  private fail(message: string) {
    // Never keep a rejected password around; put the cursor back in the password box.
    this.password.set('');
    this.confirm.set('');
    this.error.set(message);
    setTimeout(() => this.passwordBox()?.nativeElement.focus());
  }
}
