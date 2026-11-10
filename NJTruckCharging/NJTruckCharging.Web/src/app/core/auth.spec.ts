import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService, SUPABASE_CLIENT_FACTORY } from './auth.service';
import { authInterceptor } from './auth.interceptor';
import { fileName } from './authenticated-downloads';
import { LOCAL_CONFIG, RUNTIME_CONFIG, RuntimeConfig, authEnabled } from './runtime-config';
import { SignInComponent } from '../features/sign-in/sign-in.component';

const CLOUD: RuntimeConfig = {
  apiBaseUrl: 'https://api.example.test', supabaseUrl: 'https://proj.supabase.co', supabasePublishableKey: 'sb_publishable_x', readOnly: true,
};

/** A stand-in for the Supabase client that records calls. */
function fakeSupabase(session: { access_token: string; user: { email: string } } | null) {
  const calls: { otp: unknown[]; verify: unknown[] } = { otp: [], verify: [] };
  let current = session;
  const client = {
    auth: {
      getSession: async () => ({ data: { session: current } }),
      onAuthStateChange: () => ({ data: { subscription: { unsubscribe() {} } } }),
      signInWithOtp: async (a: unknown) => { calls.otp.push(a); return { error: (a as { email: string }).email.startsWith('stranger') ? { status: 422 } : null }; },
      verifyOtp: async (a: { token: string }) => {
        calls.verify.push(a);
        if (a.token !== '123456') return { data: { session: null }, error: { status: 403 } };
        current = { access_token: 'tok-2', user: { email: 'pat@company.test' } };
        return { data: { session: current }, error: null };
      },
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
      { provide: SUPABASE_CLIENT_FACTORY, useValue: () => fake.client },
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  return { fake, auth: TestBed.inject(AuthService), http: TestBed.inject(HttpClient), ctl: TestBed.inject(HttpTestingController) };
}

const tick = () => new Promise(r => setTimeout(r, 0));

describe('Sign-in (Supabase, invite-only)', () => {
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

  it('asks Supabase for a code without ever creating an account', async () => {
    const { auth, fake } = setup(CLOUD);
    expect(await auth.sendCode('pat@company.test')).toBeNull();
    expect(fake.calls.otp[0]).toEqual({ email: 'pat@company.test', options: { shouldCreateUser: false, emailRedirectTo: location.origin + '/' } });
    expect(await auth.sendCode('stranger@else.test')).toContain('ask the administrator');
  });

  it('reads the export file name from Content-Disposition', () => {
    expect(fileName('attachment; filename=c3-sites.csv; filename*=UTF-8\'\'c3-sites%202026.csv')).toBe('c3-sites 2026.csv');
    expect(fileName('attachment; filename="compare.csv"')).toBe('compare.csv');
    expect(fileName(null)).toBe('export.csv');
  });
});

describe('SignInComponent', () => {
  it('walks through e-mail → code → signed in, with labelled fields and plain error messages', async () => {
    const { auth, fake } = setup(CLOUD);
    const f = TestBed.createComponent(SignInComponent);
    await tick(); f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('h1')!.textContent).toContain('Sign in');
    expect(el.querySelector('label[for="si-email"]')!.textContent).toContain('Work e-mail');

    const email = el.querySelector('#si-email') as HTMLInputElement;
    email.value = '  Pat@Company.test '; email.dispatchEvent(new Event('input')); f.detectChanges();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await tick(); await tick(); f.detectChanges();
    expect(fake.calls.otp[0]).toEqual({ email: 'pat@company.test', options: { shouldCreateUser: false, emailRedirectTo: location.origin + '/' } });
    expect(el.querySelector('h1')!.textContent).toContain('Enter your code');

    const code = el.querySelector('#si-code') as HTMLInputElement;
    code.value = '000000'; code.dispatchEvent(new Event('input')); f.detectChanges();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await tick(); await tick(); f.detectChanges();
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('did not work');
    expect(auth.state()).toBe('signed-out');

    code.value = '123 456'; code.dispatchEvent(new Event('input')); f.detectChanges();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await tick(); await tick();
    expect(auth.state()).toBe('signed-in');
  });
});
