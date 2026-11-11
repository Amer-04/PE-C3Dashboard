import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AUTH_LOCATION, AuthService, CHECK_EMAIL_MESSAGE, SUPABASE_CLIENT_FACTORY, WORK_EMAIL_MESSAGE, signInMessage, signUpMessage } from './auth.service';
import { authInterceptor } from './auth.interceptor';
import { fileName } from './authenticated-downloads';
import { LOCAL_CONFIG, RUNTIME_CONFIG, RuntimeConfig, authEnabled } from './runtime-config';
import { SignInComponent } from '../features/sign-in/sign-in.component';

const CLOUD: RuntimeConfig = {
  apiBaseUrl: 'https://api.example.test', supabaseUrl: 'https://proj.supabase.co', supabasePublishableKey: 'sb_publishable_x', readOnly: true,
  selfRegistration: true,
};
/** The deployment before self-registration is opened (sign-ups still off in Supabase). */
const CLOSED: RuntimeConfig = { ...CLOUD, selfRegistration: false };
const GOOD_PW = 'Correct-Horse-42';

type Err = { status?: number; code?: string } | null;

/** A stand-in for the Supabase client that records calls. */
function fakeSupabase(session: { access_token: string; user: { email: string } } | null) {
  const calls = {
    password: [] as { email: string; password: string }[],
    signUp: [] as { email: string; password: string; options?: { emailRedirectTo?: string } }[],
    resend: [] as { type: string; email: string }[],
    otp: [] as unknown[],
    exchange: [] as unknown[],
    options: [] as unknown[],
  };
  let current = session;
  /** What the server answers to signUp (e.g. the Before User Created hook refusing). */
  const answers = { signUp: null as Err, resend: null as Err };
  const client = {
    auth: {
      getSession: async () => ({ data: { session: current } }),
      onAuthStateChange: () => ({ data: { subscription: { unsubscribe() {} } } }),
      signInWithPassword: async (a: { email: string; password: string }) => {
        calls.password.push(a);
        if (a.email === 'pat@poweredison.com' && a.password === GOOD_PW) {
          current = { access_token: 'tok-2', user: { email: 'pat@poweredison.com' } };
          return { data: { session: current, user: current.user }, error: null };
        }
        if (a.email === 'new@ev-edison.com') return { data: { session: null, user: null }, error: { status: 400, code: 'email_not_confirmed' } };
        return { data: { session: null, user: null }, error: { status: 400, code: 'invalid_credentials' } };
      },
      signUp: async (a: { email: string; password: string }) => {
        calls.signUp.push(a);
        // Supabase never signs a new user in before the address is confirmed: no session here.
        return { data: { user: answers.signUp ? null : { id: 'u', identities: [] }, session: null }, error: answers.signUp };
      },
      resend: async (a: { type: string; email: string }) => { calls.resend.push(a); return { data: {}, error: answers.resend }; },
      // Must never be called by the app: recorded so the tests can prove it.
      signInWithOtp: async (a: unknown) => { calls.otp.push(a); return { error: null }; },
      exchangeCodeForSession: async (a: unknown) => { calls.exchange.push(a); return { data: {}, error: null }; },
      signOut: async () => { current = null; return { error: null }; },
    },
  };
  return { client, calls, answers };
}

function setup(config: RuntimeConfig, session: Parameters<typeof fakeSupabase>[0] = null, href = 'https://c3.example.test/') {
  const fake = fakeSupabase(session);
  const where = { href, origin: new URL(href).origin, replaced: [] as string[], replace(u: string) { this.replaced.push(u); } };
  TestBed.configureTestingModule({
    providers: [
      { provide: RUNTIME_CONFIG, useValue: config },
      {
        provide: SUPABASE_CLIENT_FACTORY,
        useValue: (_url: string, _key: string, options: unknown) => { fake.calls.options.push(options); return fake.client; },
      },
      { provide: AUTH_LOCATION, useValue: where },
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  return { fake, where, auth: TestBed.inject(AuthService), http: TestBed.inject(HttpClient), ctl: TestBed.inject(HttpTestingController) };
}

const tick = () => new Promise(r => setTimeout(r, 0));

describe('Sign-in (Supabase, e-mail + password)', () => {
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
    const { auth, http, ctl } = setup(CLOUD, { access_token: 'tok-1', user: { email: 'pat@poweredison.com' } });
    await tick();
    expect(auth.state()).toBe('signed-in');
    http.get('/api/sites/export').subscribe();
    await tick();
    const req = ctl.expectOne('https://api.example.test/api/sites/export');
    expect(req.request.headers.get('Authorization')).toBe('Bearer tok-1');
    req.flush('');
  });

  it('never sends the token anywhere except the API', async () => {
    const { http, ctl } = setup(CLOUD, { access_token: 'tok-1', user: { email: 'pat@poweredison.com' } });
    await tick();
    http.get('https://tile.openstreetmap.org/1/1/1.png').subscribe();
    await tick();
    const req = ctl.expectOne('https://tile.openstreetmap.org/1/1/1.png');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush('');
  });

  it('returns to the sign-in screen when the API answers 401 (expired or revoked session, deleted account)', async () => {
    const { auth, http, ctl } = setup(CLOUD, { access_token: 'old', user: { email: 'pat@poweredison.com' } });
    await tick();
    http.get('/api/sites').subscribe({ error: () => {} });
    await tick();
    ctl.expectOne('https://api.example.test/api/sites').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(auth.state()).toBe('signed-out');
    expect(auth.notice()).toContain('session has ended');
  });

  it('shows "no access" for 403 not_approved, but not for read-only refusals or a 503 sign-in-check outage', async () => {
    const { auth, http, ctl } = setup(CLOUD, { access_token: 't', user: { email: 'pat@gmail.com' } });
    await tick();
    http.post('/api/saved-sites', {}).subscribe({ error: () => {} });
    await tick();
    ctl.expectOne('https://api.example.test/api/saved-sites').flush({ code: 'read_only' }, { status: 403, statusText: 'Forbidden' });
    expect(auth.state()).toBe('signed-in');

    http.get('/api/sites').subscribe({ error: () => {} });
    await tick();
    ctl.expectOne('https://api.example.test/api/sites').flush({ code: 'auth_unavailable' }, { status: 503, statusText: 'Unavailable' });
    expect(auth.state()).toBe('signed-in');                       // keeps the session; the page shows its own error

    http.get('/api/sites').subscribe({ error: () => {} });
    await tick();
    ctl.expectOne('https://api.example.test/api/sites').flush({ code: 'not_approved' }, { status: 403, statusText: 'Forbidden' });
    expect(auth.state()).toBe('denied');
  });

  it('signs in with e-mail + password; never uses e-mailed codes', async () => {
    const { auth, fake } = setup(CLOUD);
    expect(await auth.signIn('pat@poweredison.com', GOOD_PW)).toBeNull();
    expect(fake.calls.password).toEqual([{ email: 'pat@poweredison.com', password: GOOD_PW }]);
    expect(auth.state()).toBe('signed-in');
    expect(auth.email()).toBe('pat@poweredison.com');
    expect(fake.calls.signUp).toEqual([]);
    expect(fake.calls.otp).toEqual([]);
  });

  it('gives the same message for an unknown e-mail and a wrong password (no account enumeration)', async () => {
    const { auth } = setup(CLOUD);
    const unknown = await auth.signIn('stranger@poweredison.com', 'whatever-123');
    const wrong = await auth.signIn('pat@poweredison.com', 'not-the-password');
    expect(unknown).toBe('The e-mail address or password is not correct.');
    expect(wrong).toBe(unknown);
    expect(auth.state()).toBe('signed-out');
  });

  it('an unconfirmed account cannot sign in and is told to open the link', async () => {
    const { auth } = setup(CLOUD);
    expect(await auth.signIn('new@ev-edison.com', GOOD_PW)).toContain('confirm your e-mail address first');
    expect(auth.state()).toBe('signed-out');
  });

  it('explains rate limits, unconfirmed and disabled accounts, and outages in plain words', () => {
    expect(signInMessage({ status: 429, code: 'over_request_rate_limit' })).toContain('Too many sign-in attempts');
    expect(signInMessage({ status: 400, code: 'email_not_confirmed' })).toContain('confirm your e-mail');
    expect(signInMessage({ status: 400, code: 'user_banned' })).toContain('disabled');
    expect(signInMessage({ status: 503 })).toContain('not available right now');
    expect(signInMessage({ status: 400, code: 'invalid_credentials' })).toBe('The e-mail address or password is not correct.');
  });

  it('uses PKCE and never accepts sign-in tokens from the address bar', () => {
    const { fake } = setup(CLOUD);
    expect(fake.calls.options[0]).toEqual({
      auth: { persistSession: true, autoRefreshToken: true, detectSessionInUrl: false, flowType: 'pkce' },
    });
  });

  it('restores a saved session on reload', async () => {
    const { auth } = setup(CLOUD, { access_token: 'tok-1', user: { email: 'pat@poweredison.com' } });
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

describe('Create account (self-registration with a work e-mail address)', () => {
  it.each(['new.hire@poweredison.com', 'new.hire@ev-edison.com', 'New.Hire@PowerEdison.com'])(
    'accepts %s and asks Supabase to e-mail a confirmation link back to this site', async (email) => {
      const { auth, fake } = setup(CLOUD);
      expect(await auth.signUp(email, GOOD_PW)).toEqual({ ok: true });
      expect(fake.calls.signUp).toEqual([{ email, password: GOOD_PW, options: { emailRedirectTo: 'https://c3.example.test/' } }]);
      expect(auth.state()).toBe('signed-out');                   // no session until the address is confirmed and they sign in
    });

  it.each(['x@gmail.com', 'x@poweredison.co', 'x@mail.poweredison.com', 'x@poweredison.com.evil.io', 'x@poweredіson.com'])(
    'refuses %s before contacting Supabase (convenience only; the hook and API enforce it)', async (email) => {
      const { auth, fake } = setup(CLOUD);
      expect(await auth.signUp(email, GOOD_PW)).toEqual({ ok: false, message: WORK_EMAIL_MESSAGE });
      expect(fake.calls.signUp).toEqual([]);
    });

  it('checks the password before contacting Supabase', async () => {
    const { auth, fake } = setup(CLOUD);
    const r = await auth.signUp('pat@poweredison.com', 'short1');
    expect(r.ok).toBe(false);
    expect(!r.ok && r.message).toContain('at least 12 characters');
    expect(fake.calls.signUp).toEqual([]);
  });

  it('shows the server refusal (Before User Created hook) as the work-address message, and other errors plainly', async () => {
    const { auth, fake } = setup(CLOUD);
    fake.answers.signUp = { status: 403, code: 'unexpected_failure' };
    expect(await auth.signUp('pat@poweredison.com', GOOD_PW)).toEqual({ ok: false, message: WORK_EMAIL_MESSAGE });
    expect(signUpMessage({ status: 422, code: 'weak_password' })).toContain('too weak');
    expect(signUpMessage({ status: 422, code: 'signup_disabled' })).toContain('not open yet');
    expect(signUpMessage({ status: 429, code: 'over_email_send_rate_limit' })).toContain('Too many');
    expect(signUpMessage({ status: 500 })).toContain('not available right now');
  });

  it('gives the same answer whether or not the address already has an account (no enumeration)', async () => {
    const { auth } = setup(CLOUD);
    // Supabase answers an existing address with a look-alike user and no error; the app cannot and does not tell.
    expect(await auth.signUp('pat@poweredison.com', GOOD_PW)).toEqual({ ok: true });
    expect(await auth.signUp('brand.new@poweredison.com', GOOD_PW)).toEqual({ ok: true });
    expect(await auth.resendConfirmation('pat@poweredison.com')).toBe(CHECK_EMAIL_MESSAGE);
    expect(await auth.resendConfirmation('nobody@ev-edison.com')).toBe(CHECK_EMAIL_MESSAGE);
  });

  it('does not ask Supabase to e-mail non-work addresses, but answers the same way', async () => {
    const { auth, fake } = setup(CLOUD);
    expect(await auth.resendConfirmation('x@gmail.com')).toBe(CHECK_EMAIL_MESSAGE);
    expect(fake.calls.resend).toEqual([]);
  });
});

describe('Before self-registration is opened (C3_SELF_REGISTRATION not "true")', () => {
  it('hides Create account, keeps sign-in, and never calls sign-up or resend', async () => {
    const { auth, fake } = setup(CLOSED);
    const f = TestBed.createComponent(SignInComponent);
    await tick(); f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('#si-tab-create')).toBeNull();
    expect(el.textContent).not.toContain('Create account');
    expect(el.textContent).not.toContain('confirmation link');
    expect(el.querySelector('#si-email')).not.toBeNull();
    expect((await auth.signUp('pat@poweredison.com', GOOD_PW)).ok).toBe(false);
    expect(await auth.resendConfirmation('pat@poweredison.com')).toBe(CHECK_EMAIL_MESSAGE);
    expect(fake.calls.signUp).toEqual([]);
    expect(fake.calls.resend).toEqual([]);
  });
});

describe('Returning from the confirmation e-mail', () => {
  it('shows "confirmed, please sign in", removes the code from the address bar and does NOT sign in with it', async () => {
    const { auth, where, fake } = setup(CLOUD, null, 'https://c3.example.test/?code=one-time-code-123');
    await tick();
    expect(auth.notice()).toContain('e-mail address is confirmed');
    expect(where.replaced).toEqual(['/']);
    expect(fake.calls.exchange).toEqual([]);
    expect(auth.state()).toBe('signed-out');
  });

  it('removes tokens and errors from the fragment and explains an expired link', async () => {
    const { auth, where } = setup(CLOUD, null,
      'https://c3.example.test/#error=access_denied&error_code=otp_expired&error_description=Email+link+is+invalid+or+has+expired');
    await tick();
    expect(auth.notice()).toContain('no longer valid');
    expect(where.replaced).toEqual(['/']);

    TestBed.resetTestingModule();
    const legacy = setup(CLOUD, null, 'https://c3.example.test/?keep=1#access_token=eyJ.leaked.token&refresh_token=r&type=signup');
    await tick();
    expect(legacy.where.replaced).toEqual(['/?keep=1']);
    expect(legacy.auth.state()).toBe('signed-out');              // an access token in a link is never used
  });

  it('leaves ordinary page addresses alone', async () => {
    const { auth, where } = setup(CLOUD, null, 'https://c3.example.test/compare?siteIds=A&siteIds=B');
    await tick();
    expect(auth.notice()).toBeNull();
    expect(where.replaced).toEqual([]);
  });
});

describe('SignInComponent', () => {
  function input(el: HTMLElement, sel: string, value: string) {
    const box = el.querySelector(sel) as HTMLInputElement;
    box.value = value; box.dispatchEvent(new Event('input'));
    return box;
  }

  it('signs in with work e-mail + password, with labelled fields, a show/hide toggle and plain errors', async () => {
    const { auth, fake } = setup(CLOUD);
    const f = TestBed.createComponent(SignInComponent);
    await tick(); f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('h1')!.textContent).toContain('Sign in');
    expect(el.querySelector('label[for="si-email"]')!.textContent).toContain('Work e-mail');
    expect(el.querySelector('label[for="si-password"]')!.textContent).toContain('Password');
    const email = el.querySelector('#si-email') as HTMLInputElement;
    const password = el.querySelector('#si-password') as HTMLInputElement;
    expect(email.getAttribute('autocomplete')).toBe('username');
    expect(password.getAttribute('autocomplete')).toBe('current-password');
    expect(password.type).toBe('password');

    const toggle = el.querySelector('.si-show') as HTMLButtonElement;
    toggle.click(); f.detectChanges();
    expect(password.type).toBe('text');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    toggle.click(); f.detectChanges();
    expect(password.type).toBe('password');

    input(el, '#si-email', '  Pat@PowerEdison.com ');
    input(el, '#si-password', 'not-the-password'); f.detectChanges();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await tick(); await tick(); f.detectChanges();
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('e-mail address or password is not correct');
    expect((el.querySelector('#si-password') as HTMLInputElement).value).toBe('');
    expect(auth.state()).toBe('signed-out');

    input(el, '#si-password', GOOD_PW); f.detectChanges();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await tick(); await tick();
    expect(fake.calls.password.at(-1)).toEqual({ email: 'pat@poweredison.com', password: GOOD_PW });
    expect(auth.state()).toBe('signed-in');
    expect(fake.calls.signUp).toEqual([]);
  });

  it('Create account: work-address hint, password rules, matching confirmation, then "check your e-mail"', async () => {
    const { fake } = setup(CLOUD);
    const f = TestBed.createComponent(SignInComponent);
    await tick(); f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    (el.querySelector('#si-tab-create') as HTMLButtonElement).click(); f.detectChanges();
    expect(el.querySelector('h1')!.textContent).toContain('Create account');
    expect(el.querySelector('#si-tab-create')!.getAttribute('aria-selected')).toBe('true');
    expect((el.querySelector('#si-password') as HTMLInputElement).getAttribute('autocomplete')).toBe('new-password');
    const submit = () => el.querySelector('button[type="submit"]') as HTMLButtonElement;

    input(el, '#si-email', 'pat@gmail.com'); f.detectChanges();
    expect(el.querySelector('#si-email-hint')!.textContent).toContain('work e-mail address');
    expect(el.querySelector('#si-email')!.getAttribute('aria-invalid')).toBe('true');

    input(el, '#si-email', 'Pat@EV-Edison.com');
    input(el, '#si-password', 'short'); f.detectChanges();
    expect(el.querySelector('#si-email-hint')).toBeNull();
    expect(el.querySelector('#si-pw-rules')!.textContent).toContain('Still needed');
    expect(submit().disabled).toBe(true);

    input(el, '#si-password', GOOD_PW);
    input(el, '#si-confirm', GOOD_PW + 'x'); f.detectChanges();
    expect(el.textContent).toContain('not the same');
    expect(submit().disabled).toBe(true);

    input(el, '#si-confirm', GOOD_PW); f.detectChanges();
    expect(submit().disabled).toBe(false);
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await tick(); await tick(); f.detectChanges();
    expect(fake.calls.signUp).toEqual([{ email: 'Pat@EV-Edison.com', password: GOOD_PW, options: { emailRedirectTo: 'https://c3.example.test/' } }]);
    expect(el.querySelector('h1')!.textContent).toContain('Check your e-mail');
    expect(el.querySelector('[role="status"]')!.textContent).toContain(CHECK_EMAIL_MESSAGE);

    (Array.from(el.querySelectorAll('button')).find(b => b.textContent!.includes('Send the link again')) as HTMLButtonElement).click();
    await tick(); await tick();
    expect(fake.calls.resend).toEqual([{ type: 'signup', email: 'Pat@EV-Edison.com', options: { emailRedirectTo: 'https://c3.example.test/' } }]);
  });

  it('Create account: a server refusal is shown and both password boxes are emptied', async () => {
    const { fake } = setup(CLOUD);
    fake.answers.signUp = { status: 403, code: 'unexpected_failure' };
    const f = TestBed.createComponent(SignInComponent);
    await tick(); f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    (el.querySelector('#si-tab-create') as HTMLButtonElement).click(); f.detectChanges();
    input(el, '#si-email', 'pat@poweredison.com');
    input(el, '#si-password', GOOD_PW);
    input(el, '#si-confirm', GOOD_PW); f.detectChanges();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await tick(); await tick(); f.detectChanges();
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('work e-mail address');
    expect((el.querySelector('#si-password') as HTMLInputElement).value).toBe('');
    expect((el.querySelector('#si-confirm') as HTMLInputElement).value).toBe('');
  });

  it('the "no access" screen is generic and offers sign-out', async () => {
    const { auth } = setup(CLOUD, { access_token: 't', user: { email: 'pat@gmail.com' } });
    await tick();
    auth.notApproved();
    const f = TestBed.createComponent(SignInComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('h1')!.textContent).toContain('does not have access');
    expect(el.textContent).toContain('pat@gmail.com');
    expect(el.textContent).not.toMatch(/domain/i);
  });
});
