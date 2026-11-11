import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { createClient, type Session, type SupabaseClient } from '@supabase/supabase-js';
import { RUNTIME_CONFIG, authEnabled } from './runtime-config';
import { isEmployeeEmail, passwordProblems } from './employee-email';

/** Creates the Supabase client (replaceable in tests). */
export const SUPABASE_CLIENT_FACTORY = new InjectionToken<typeof createClient>('SUPABASE_CLIENT_FACTORY', {
  providedIn: 'root', factory: () => createClient,
});

/** Where the browser is (replaceable in tests): used to read and clean up the e-mail confirmation link. */
export const AUTH_LOCATION = new InjectionToken<Pick<Location, 'href' | 'origin'> & { replace(url: string): void }>('AUTH_LOCATION', {
  providedIn: 'root',
  factory: () => ({
    get href() { return location.href; },
    get origin() { return location.origin; },
    replace: (url: string) => history.replaceState(history.state, '', url),
  }),
});

/** loading → signed-out ⇄ signed-in; "denied" = signed in, but the server says this account has no access. */
export type AuthState = 'loading' | 'signed-out' | 'signed-in' | 'denied';

export type SignUpResult = { ok: true } | { ok: false; message: string };

/** The one message shown after "Create account" and "Send the link again", whatever happened (no account enumeration). */
export const CHECK_EMAIL_MESSAGE =
  'If this address can be registered, we have sent it a confirmation link. Open the link, then sign in here with your password.';
export const WORK_EMAIL_MESSAGE = 'Please use your Power Edison or EV Edison work e-mail address (…@poweredison.com or …@ev-edison.com).';

/**
 * Supabase e-mail + password accounts for employees. Anyone with a work address at exactly poweredison.com or
 * ev-edison.com can create an account; it works only after they open the confirmation link sent to that address. The
 * checks here are for convenience: Supabase (Before User Created hook) and the API (every request) enforce the rule.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly config = inject(RUNTIME_CONFIG);
  readonly enabled = authEnabled(this.config);
  readonly state = signal<AuthState>(this.enabled ? 'loading' : 'signed-in');
  readonly email = signal<string | null>(null);
  /** Shown on the sign-in screen (session ended, e-mail confirmed, link expired). */
  readonly notice = signal<string | null>(null);
  private readonly where = inject(AUTH_LOCATION);

  private readonly client: SupabaseClient | null = this.enabled
    ? inject(SUPABASE_CLIENT_FACTORY)(this.config.supabaseUrl, this.config.supabasePublishableKey, {
        auth: {
          persistSession: true,
          autoRefreshToken: true,
          // Links in e-mails never sign anyone in here: the confirmation link only proves the address, then the user
          // signs in with their password. PKCE means a link carries a one-time code, never an access token.
          detectSessionInUrl: false,
          flowType: 'pkce',
        },
      })
    : null;

  constructor() {
    if (!this.client) return;
    this.consumeEmailLink();
    this.client.auth.getSession().then(({ data }) => this.apply(data.session));
    this.client.auth.onAuthStateChange((_event, session) => {
      if (this.state() !== 'denied' || !session) this.apply(session);
    });
  }

  /** Full API URL for an /api path (the API runs on its own host in the cloud deployment). */
  apiUrl(path: string): string {
    return path.startsWith('/api') ? this.config.apiBaseUrl + path : path;
  }

  /** A current access token (refreshed by the Supabase client when needed); null when signed out or auth is off. */
  async accessToken(): Promise<string | null> {
    if (!this.client) return null;
    const { data } = await this.client.auth.getSession();
    return data.session?.access_token ?? null;
  }

  /**
   * Sign in with e-mail and password. Returns null on success, otherwise a plain message. Wrong e-mail and wrong
   * password get the same message, so the screen never reveals which accounts exist.
   */
  async signIn(email: string, password: string): Promise<string | null> {
    if (!this.client) return null;
    const { data, error } = await this.client.auth.signInWithPassword({ email, password });
    if (error || !data.session) return signInMessage(error);
    this.notice.set(null);
    this.apply(data.session);
    return null;
  }

  /**
   * Create an account. The address must be a work address (checked again by Supabase and the API). On success Supabase
   * e-mails a confirmation link; the account cannot be used until it is opened. Whether the address already had an
   * account is never revealed: the same "check your e-mail" answer is given either way.
   */
  async signUp(email: string, password: string): Promise<SignUpResult> {
    if (!this.client || !this.config.selfRegistration) return { ok: false, message: 'Creating accounts is not open yet. Please ask the administrator.' };
    if (!isEmployeeEmail(email)) return { ok: false, message: WORK_EMAIL_MESSAGE };
    const problems = passwordProblems(password);
    if (problems.length) return { ok: false, message: `The password needs ${problems.join(', ')}.` };
    const { error } = await this.client.auth.signUp({ email, password, options: { emailRedirectTo: this.where.origin + '/' } });
    return error ? { ok: false, message: signUpMessage(error) } : { ok: true };
  }

  /** Send the confirmation link again. Always answers the same way (never says whether the address exists). */
  async resendConfirmation(email: string): Promise<string> {
    if (this.client && this.config.selfRegistration && isEmployeeEmail(email)) {
      const { error } = await this.client.auth.resend({ type: 'signup', email, options: { emailRedirectTo: this.where.origin + '/' } });
      if (error && (error.status === 429 || error.code === 'over_email_send_rate_limit' || error.code === 'over_request_rate_limit')) {
        return 'Too many e-mails were requested. Please wait a few minutes and try again.';
      }
    }
    return CHECK_EMAIL_MESSAGE;
  }

  async signOut(): Promise<void> {
    if (this.client) await this.client.auth.signOut({ scope: 'local' });
    // Start clean: no data from the previous session stays in memory.
    location.reload();
  }

  /** The API refused the token (expired, revoked or invalid): back to the sign-in screen. */
  sessionRejected(): void {
    if (!this.client || this.state() === 'signed-out') return;
    this.notice.set('Your session has ended. Please sign in again.');
    this.client.auth.signOut({ scope: 'local' });
    this.state.set('signed-out');
  }

  /** Signed in, but the API says this account has no access. */
  notApproved(): void {
    if (this.enabled) this.state.set('denied');
  }

  private apply(session: Session | null): void {
    this.email.set(session?.user.email ?? null);
    this.state.set(session ? 'signed-in' : 'signed-out');
  }

  /**
   * Arriving from the confirmation e-mail: Supabase has already confirmed the address (or says why not) and sends the
   * browser back with ?code=… or #error=…. Nothing in the link is used to sign in; it is removed from the address bar
   * and history, and a plain notice is shown.
   */
  private consumeEmailLink(): void {
    const url = new URL(this.where.href);
    const hash = new URLSearchParams(url.hash.replace(/^#/, ''));
    const keys = ['code', 'error', 'error_code', 'error_description', 'access_token', 'refresh_token', 'expires_at',
      'expires_in', 'token_type', 'type', 'provider_token', 'sb'];
    const has = (k: string) => url.searchParams.has(k) || hash.has(k);
    if (!keys.some(has)) return;
    const failed = has('error') || has('error_code');
    this.notice.set(failed
      ? 'That link is no longer valid (links expire and work only once). Sign in, or ask for a new confirmation link below.'
      : 'Your e-mail address is confirmed. Please sign in with your password.');
    for (const k of keys) url.searchParams.delete(k);
    url.hash = '';
    this.where.replace(url.pathname + url.search);
  }
}

/** Plain-language message for a failed sign-in (never says whether the e-mail address exists). */
export function signInMessage(error: { status?: number; code?: string } | null): string {
  if (error?.status === 429 || error?.code === 'over_request_rate_limit') {
    return 'Too many sign-in attempts. Please wait a few minutes and try again.';
  }
  if (error?.code === 'email_not_confirmed') {
    return 'Please confirm your e-mail address first: open the link we sent you. You can ask for a new link below.';
  }
  if (error?.code === 'user_banned') {
    return 'This account is disabled. Please ask the administrator.';
  }
  if (error && (error.status === undefined || error.status >= 500 || error.status === 0)) {
    return 'Sign-in is not available right now. Please try again in a few minutes.';
  }
  return 'The e-mail address or password is not correct.';
}

/** Plain-language message for a failed sign-up. Never says whether the address already has an account. */
export function signUpMessage(error: { status?: number; code?: string } | null): string {
  if (error?.status === 429 || error?.code === 'over_request_rate_limit' || error?.code === 'over_email_send_rate_limit') {
    return 'Too many attempts. Please wait a few minutes and try again.';
  }
  if (error?.code === 'weak_password') {
    return 'This password is too weak or too common. Please choose a longer, less predictable one.';
  }
  if (error?.code === 'signup_disabled' || error?.code === 'email_provider_disabled') {
    return 'Creating accounts is not open yet. Please ask the administrator.';
  }
  if (error && (error.status === undefined || error.status >= 500 || error.status === 0)) {
    return 'Creating accounts is not available right now. Please try again in a few minutes.';
  }
  // Refused by the Before User Created hook, an invalid address, or anything else the server rejected.
  return WORK_EMAIL_MESSAGE;
}
