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
 * Supabase e-mail + password sign-in for employee accounts the administrator created. Nothing here can create an
 * account (no sign-up call exists), no e-mail is ever sent, and the API additionally checks every request against its
 * own allowlist of approved user ids.
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
        // Password sign-in uses no e-mailed links, so tokens in a URL are never accepted (old confirmation or magic
        // links that land on this site are ignored).
        auth: { persistSession: true, autoRefreshToken: true, detectSessionInUrl: false },
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
   * Sign in with the e-mail and password the administrator set up. Returns null on success, otherwise a plain message.
   * Wrong e-mail and wrong password get the same message, so the screen never reveals which accounts exist.
   */
  async signIn(email: string, password: string): Promise<string | null> {
    if (!this.client) return null;
    const { data, error } = await this.client.auth.signInWithPassword({ email, password });
    if (error || !data.session) return signInMessage(error);
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

/** Plain-language message for a failed sign-in (never says whether the e-mail address exists). */
export function signInMessage(error: { status?: number; code?: string } | null): string {
  if (error?.status === 429 || error?.code === 'over_request_rate_limit') {
    return 'Too many sign-in attempts. Please wait a few minutes and try again.';
  }
  if (error?.code === 'email_not_confirmed') {
    return 'This account is not activated yet. Please ask the administrator to activate it.';
  }
  if (error?.code === 'user_banned') {
    return 'This account is disabled. Please ask the administrator.';
  }
  if (error && (error.status === undefined || error.status >= 500 || error.status === 0)) {
    return 'Sign-in is not available right now. Please try again in a few minutes.';
  }
  return 'The e-mail address or password is not correct.';
}
