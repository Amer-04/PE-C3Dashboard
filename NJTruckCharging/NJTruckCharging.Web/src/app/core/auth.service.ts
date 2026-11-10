import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { createClient, type Session, type SupabaseClient } from '@supabase/supabase-js';
import { RUNTIME_CONFIG, authEnabled } from './runtime-config';

/** Creates the Supabase client (replaceable in tests). */
export const SUPABASE_CLIENT_FACTORY = new InjectionToken<typeof createClient>('SUPABASE_CLIENT_FACTORY', {
  providedIn: 'root', factory: () => createClient,
});

/** loading → signed-out ⇄ signed-in; "denied" = signed in, but the server says this account is not approved. */
export type AuthState = 'loading' | 'signed-out' | 'signed-in' | 'denied';

/**
 * Supabase e-mail sign-in (one-time code). Accounts are never created here: only people the administrator invited
 * can receive a code, and the API additionally checks every request against its own allowlist of approved user ids.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly config = inject(RUNTIME_CONFIG);
  readonly enabled = authEnabled(this.config);
  readonly state = signal<AuthState>(this.enabled ? 'loading' : 'signed-in');
  readonly email = signal<string | null>(null);
  /** Shown on the sign-in screen after the session ended (expired, revoked or signed out elsewhere). */
  readonly notice = signal<string | null>(null);

  private readonly client: SupabaseClient | null = this.enabled
    ? inject(SUPABASE_CLIENT_FACTORY)(this.config.supabaseUrl, this.config.supabasePublishableKey, {
        // detectSessionInUrl: the e-mailed sign-in link (if the shared project's template sends one) returns to THIS site.
        auth: { persistSession: true, autoRefreshToken: true, detectSessionInUrl: true },
      })
    : null;

  constructor() {
    if (!this.client) return;
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
   * Step 1: e-mail a one-time code (and, if the project's template includes one, a sign-in link back to this site).
   * shouldCreateUser:false — an address that was not invited never gets an account.
   */
  async sendCode(email: string): Promise<string | null> {
    if (!this.client) return null;
    const { error } = await this.client.auth.signInWithOtp({
      email, options: { shouldCreateUser: false, emailRedirectTo: location.origin + '/' },
    });
    if (!error) return null;
    if (error.status === 429) return 'Too many attempts. Please wait a few minutes and try again.';
    return 'We could not send a code to that address. Check the spelling, or ask the administrator to invite you.';
  }

  /** Step 2: check the code from the e-mail. */
  async verifyCode(email: string, code: string): Promise<string | null> {
    if (!this.client) return null;
    const { data, error } = await this.client.auth.verifyOtp({ email, token: code, type: 'email' });
    if (error || !data.session) return 'That code did not work. Check it, or send a new code.';
    this.notice.set(null);
    this.apply(data.session);
    return null;
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

  /** Signed in, but this account is not on the server's approved list. */
  notApproved(): void {
    if (this.enabled) this.state.set('denied');
  }

  private apply(session: Session | null): void {
    this.email.set(session?.user.email ?? null);
    this.state.set(session ? 'signed-in' : 'signed-out');
  }
}
