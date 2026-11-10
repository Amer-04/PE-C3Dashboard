import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService, SUPABASE_CLIENT_FACTORY, signInMessage } from './auth.service';
import { authInterceptor } from './auth.interceptor';
import { fileName } from './authenticated-downloads';
import { LOCAL_CONFIG, RUNTIME_CONFIG, RuntimeConfig, authEnabled } from './runtime-config';
import { SignInComponent } from '../features/sign-in/sign-in.component';

const CLOUD: RuntimeConfig = {
  apiBaseUrl: 'https://api.example.test', supabaseUrl: 'https://proj.supabase.co', supabasePublishableKey: 'sb_publishable_x', readOnly: true,
};

/** A stand-in for the Supabase client that records calls. */
function fakeSupabase(session: { access_token: string; user: { email: string } } | null) {
  const calls: { password: { email: string; password: string }[]; signUp: unknown[]; otp: unknown[]; options: unknown[] } =
    { password: [], signUp: [], otp: [], options: [] };
  let current = session;
  const client = {
    auth: {
      getSession: async () => ({ data: { session: current } }),
      onAuthStateChange: () => ({ data: { subscription: { unsubscribe() {} } } }),
      signInWithPassword: async (a: { email: string; password: string }) => {
        calls.password.push(a);
        if (a.email === 'pat@company.test' && a.password === 'Correct-Horse-42') {
          current = { access_token: 'tok-2', user: { email: 'pat@company.test' } };
          return { data: { session: current, user: current.user }, error: null };
        }
        return { data: { session: null, user: null }, error: { status: 400, code: 'invalid_credentials' } };
      },
      // Must never be called by the app: recorded so the tests can prove it.
      signUp: async (a: unknown) => { calls.signUp.push(a); return { data: {}, error: null }; },
      signInWithOtp: async (a: unknown) => { calls.otp.push(a); return { error: null }; },
      signOut: async () => { current = null; return { error: null }; },
    },
  };
  return { client, calls };
}

function setup(config: RuntimeConfig, session: Parameters<typeof fakeSupabase>[0] = null) {
  const fake = fakeSupabase(session);
  TestBed.configureTestingModule({
    providers: [
      { provide: RUNTIME_CONFIG, useValue: config },
      {
        provide: SUPABASE_CLIENT_FACTORY,
        useValue: (_url: string, _key: string, options: unknown) => { fake.calls.options.push(options); return fake.client; },
      },
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  return { fake, auth: TestBed.inject(AuthService), http: TestBed.inject(HttpClient), ctl: TestBed.inject(HttpTestingController) };
}

const tick = () => new Promise(r => setTimeout(r, 0));

describe('Sign-in (Supabase, administrator-created accounts, e-mail + password)', () => {
  it('is off without a deployment config: local development works as before, same-origin, no token', async () => {
    const { auth, http, ctl } = setup(LOCAL_CONFIG);
    expect(authEnabled(LOCAL_CONFIG)).toBe(false);
    expect(auth.state()).toBe('signed-in');
    http.get('/api/sites').subscribe();
    await tick();
    const req = ctl.expectOne('/api/sites');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('sends every API call to the API host with the access token', async () => {
    const { auth, http, ctl } = setup(CLOUD, { access_token: 'tok-1', user: { email: 'pat@company.test' } });
    await tick();
    expect(auth.state()).toBe('signed-in');
    http.get('/api/sites/export').subscribe();
    await tick();
    const req = ctl.expectOne('https://api.example.test/api/sites/export');
    expect(req.request.headers.get('Authorization')).toBe('Bearer tok-1');
    req.flush('');
  });

  it('never sends the token anywhere except the API', async () => {
    const { http, ctl } = setup(CLOUD, { access_token: 'tok-1', user: { email: 'pat@company.test' } });
    await tick();
    http.get('https://tile.openstreetmap.org/1/1/1.png').subscribe();
    await tick();
    const req = ctl.expectOne('https://tile.openstreetmap.org/1/1/1.png');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush('');
  });

  it('returns to the sign-in screen when the API answers 401 (expired or revoked session)', async () => {
    const { auth, http, ctl } = setup(CLOUD, { access_token: 'old', user: { email: 'pat@company.test' } });
    await tick();
    http.get('/api/sites').subscribe({ error: () => {} });
    await tick();
    ctl.expectOne('https://api.example.test/api/sites').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(auth.state()).toBe('signed-out');
    expect(auth.notice()).toContain('session has ended');
  });

  it('shows "not approved" when the API answers 403 not_approved, but not for read-only refusals', async () => {
    const { auth, http, ctl } = setup(CLOUD, { access_token: 't', user: { email: 'pat@company.test' } });
    await tick();
    http.post('/api/saved-sites', {}).subscribe({ error: () => {} });
    await tick();
    ctl.expectOne('https://api.example.test/api/saved-sites').flush({ code: 'read_only' }, { status: 403, statusText: 'Forbidden' });
    expect(auth.state()).toBe('signed-in');

    http.get('/api/sites').subscribe({ error: () => {} });
    await tick();
    ctl.expectOne('https://api.example.test/api/sites').flush({ code: 'not_approved' }, { status: 403, statusText: 'Forbidden' });
    expect(auth.state()).toBe('denied');
  });

  it('signs in with e-mail + password only: never signs up, never sends an e-mail', async () => {
    const { auth, fake } = setup(CLOUD);
    expect(await auth.signIn('pat@company.test', 'Correct-Horse-42')).toBeNull();
    expect(fake.calls.password).toEqual([{ email: 'pat@company.test', password: 'Correct-Horse-42' }]);
    expect(auth.state()).toBe('signed-in');
    expect(auth.email()).toBe('pat@company.test');
    expect(fake.calls.signUp).toEqual([]);
    expect(fake.calls.otp).toEqual([]);
  });

  it('gives the same message for an unknown e-mail and a wrong password (no account enumeration)', async () => {
    const { auth } = setup(CLOUD);
    const unknown = await auth.signIn('stranger@else.test', 'whatever-123');
    const wrong = await auth.signIn('pat@company.test', 'not-the-password');
    expect(unknown).toBe('The e-mail address or password is not correct.');
    expect(wrong).toBe(unknown);
    expect(auth.state()).toBe('signed-out');
  });

  it('explains rate limits, inactive and disabled accounts, and outages in plain words', () => {
    expect(signInMessage({ status: 429, code: 'over_request_rate_limit' })).toContain('Too many sign-in attempts');
    expect(signInMessage({ status: 400, code: 'email_not_confirmed' })).toContain('not activated yet');
    expect(signInMessage({ status: 400, code: 'user_banned' })).toContain('disabled');
    expect(signInMessage({ status: 503 })).toContain('not available right now');
    expect(signInMessage({ status: 400, code: 'invalid_credentials' })).toBe('The e-mail address or password is not correct.');
  });

  it('never accepts sign-in tokens from the address bar (old e-mailed links are ignored)', () => {
    const { fake } = setup(CLOUD);
    expect(fake.calls.options[0]).toEqual({ auth: { persistSession: true, autoRefreshToken: true, detectSessionInUrl: false } });
  });

  it('restores a saved session on reload', async () => {
    const { auth } = setup(CLOUD, { access_token: 'tok-1', user: { email: 'pat@company.test' } });
    await tick();
    expect(auth.state()).toBe('signed-in');
    expect(await auth.accessToken()).toBe('tok-1');
  });

  it('reads the export file name from Content-Disposition', () => {
    expect(fileName("attachment; filename=c3-sites.csv; filename*=UTF-8''c3-sites%202026.csv")).toBe('c3-sites 2026.csv');
    expect(fileName('attachment; filename="compare.csv"')).toBe('compare.csv');
    expect(fileName(null)).toBe('export.csv');
  });
});

describe('SignInComponent', () => {
  it('signs in with work e-mail + password, with labelled fields, a show/hide toggle and plain errors', async () => {
    const { auth, fake } = setup(CLOUD);
    const f = TestBed.createComponent(SignInComponent);
    await tick(); f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('h1')!.textContent).toContain('Sign in');
    expect(el.querySelector('label[for="si-email"]')!.textContent).toContain('Work e-mail');
    expect(el.querySelector('label[for="si-password"]')!.textContent).toContain('Password');
    expect(el.textContent).not.toContain('Send me a code');
    const email = el.querySelector('#si-email') as HTMLInputElement;
    const password = el.querySelector('#si-password') as HTMLInputElement;
    expect(email.getAttribute('autocomplete')).toBe('username');
    expect(password.getAttribute('autocomplete')).toBe('current-password');
    expect(password.type).toBe('password');

    // Show / hide password (aria-pressed reflects the state).
    const toggle = el.querySelector('.si-show') as HTMLButtonElement;
    toggle.click(); f.detectChanges();
    expect(password.type).toBe('text');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    toggle.click(); f.detectChanges();
    expect(password.type).toBe('password');

    // Wrong password: plain message, the password box is emptied.
    email.value = '  Pat@Company.test '; email.dispatchEvent(new Event('input'));
    password.value = 'not-the-password'; password.dispatchEvent(new Event('input')); f.detectChanges();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await tick(); await tick(); f.detectChanges();
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('e-mail address or password is not correct');
    expect((el.querySelector('#si-password') as HTMLInputElement).value).toBe('');
    expect(auth.state()).toBe('signed-out');

    // Correct password: signed in; the e-mail was trimmed and lower-cased.
    password.value = 'Correct-Horse-42'; password.dispatchEvent(new Event('input')); f.detectChanges();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await tick(); await tick();
    expect(fake.calls.password.at(-1)).toEqual({ email: 'pat@company.test', password: 'Correct-Horse-42' });
    expect(auth.state()).toBe('signed-in');
    expect(fake.calls.signUp).toEqual([]);
    expect(fake.calls.otp).toEqual([]);
  });
});
