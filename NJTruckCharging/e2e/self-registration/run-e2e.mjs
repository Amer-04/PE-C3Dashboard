// End-to-end test of employee self-registration, entirely on this PC. Never contacts Supabase, Render or Netlify.
//
//   throw-away PostgreSQL (54329) with sql/07 hook applied   <- the REAL Before User Created hook decides every sign-up
//   mock Supabase Auth (54321, mock-supabase-auth.mjs)        <- ES256 tokens, JWKS, confirmation links, /user
//   the real API, Release build (5173), Auth:AccessRule=EmailDomain, read-only, LocalDB test database
//   the real dashboard, production build (4300), driven in Chrome with puppeteer-core
//
// Usage (deps = a folder with: npm i pg embedded-postgres puppeteer-core@23):
//   cd NJTruckCharging.Web; npx ng build --output-path <web>
//   dotnet build NJTruckCharging/NJTruckCharging.csproj -c Release
//   node e2e/self-registration/run-e2e.mjs <deps> <web> [<screenshots dir>]
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { generateKeyPairSync, sign } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import { dirname, extname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { startMockSupabaseAuth } from './mock-supabase-auth.mjs';

const [depsArg, webArg, shotsArg] = process.argv.slice(2);
const deps = resolve(depsArg ?? '.');
const web = resolve(webArg ?? 'web-build', 'browser');
const shots = shotsArg ? resolve(shotsArg) : null;
if (shots) mkdirSync(shots, { recursive: true });
const require = createRequire(join(deps, 'package.json'));
const { default: EmbeddedPostgres } = await import(pathToFileURL(require.resolve('embedded-postgres')).href);
const pg = require('pg');
const puppeteer = require('puppeteer-core');

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, '../..');
const PORTS = { pg: 54329, auth: 54321, api: 5173, web: 4300 };
const SITE = `http://localhost:${PORTS.web}`;
const API = `http://127.0.0.1:${PORTS.api}`;
const AUTH = `http://127.0.0.1:${PORTS.auth}`;
const USER_CHECK_SECONDS = 5;
const PASSWORD = 'Correct-Horse-Battery-42';
// The one approved administrator exception (a personal Gmail account), as Auth__DomainExceptionUserIds.
const ADMIN_ID = 'a11ce000-0000-4000-8000-00000000ad01';
const ADMIN_EMAIL = 'c3.admin.personal@gmail.com';

let failures = 0, passes = 0;
const check = (ok, what) => { ok ? passes++ : failures++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}`); };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const cleanup = [];

// ---------- 1. PostgreSQL with the real hook ----------
const pgDir = join(deps, 'e2e-data');
rmSync(pgDir, { recursive: true, force: true });
const pgServer = new EmbeddedPostgres({ databaseDir: pgDir, user: 'postgres', password: 'local-test-only', port: PORTS.pg,
  persistent: false, initdbFlags: ['--encoding=UTF8', '--locale=C'], onLog: () => {}, onError: () => {} });
await pgServer.initialise();
await pgServer.start();
cleanup.push(() => pgServer.stop());
{
  const admin = new pg.Client({ host: '127.0.0.1', port: PORTS.pg, user: 'postgres', password: 'local-test-only', database: 'postgres' });
  await admin.connect();
  for (const r of ['anon', 'authenticated', 'service_role', 'authenticator']) await admin.query(`CREATE ROLE ${r} NOLOGIN`);
  await admin.query(`CREATE ROLE supabase_auth_admin LOGIN PASSWORD 'local-test-only'`);
  await admin.query(readFileSync(join(repo, 'NJTruckCharging.CloudCopy/sql/07-before-user-created-hook.sql'), 'utf8'));
  await admin.end();
}
// Supabase Auth calls the hook as supabase_auth_admin; so does the mock.
const authDb = new pg.Client({ host: '127.0.0.1', port: PORTS.pg, user: 'supabase_auth_admin', password: 'local-test-only', database: 'postgres' });
await authDb.connect();
cleanup.push(() => authDb.end());
const hook = async (event) => (await authDb.query('SELECT c3_auth.before_user_created($1::jsonb) AS r', [JSON.stringify(event)])).rows[0].r;

// ---------- 2. Mock Supabase Auth ----------
const auth = await startMockSupabaseAuth({ port: PORTS.auth, hook, siteUrl: SITE + '/' });
cleanup.push(() => auth.close());
const authPost = async (path, body, headers = {}) => {
  const r = await fetch(AUTH + path, { method: 'POST', headers: { 'Content-Type': 'application/json', apikey: 'sb_publishable_e2e', ...headers }, body: JSON.stringify(body) });
  return { status: r.status, body: await r.json().catch(() => null) };
};

// ---------- 3. Dashboard (production build) with its deployment config ----------
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.ico': 'image/x-icon', '.svg': 'image/svg+xml', '.png': 'image/png' };
const webServer = createServer((req, res) => {
  const p = new URL(req.url, SITE).pathname;
  if (p === '/app-config.json') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    return res.end(JSON.stringify({ apiBaseUrl: API, supabaseUrl: AUTH, supabasePublishableKey: 'sb_publishable_e2e', readOnly: true, selfRegistration: true }));
  }
  let file = join(web, decodeURIComponent(p));
  if (!file.startsWith(web) || !existsSync(file) || p === '/') file = join(web, 'index.html');
  res.writeHead(200, { 'Content-Type': types[extname(file)] ?? 'application/octet-stream' });
  res.end(readFileSync(file));
});
await new Promise(r => webServer.listen(PORTS.web, '127.0.0.1', r));
cleanup.push(() => new Promise(r => webServer.close(r)));

// ---------- 4. The real API ----------
const apiBin = join(repo, 'NJTruckCharging/bin/Release/net10.0');
const api = spawn('dotnet', [join(apiBin, 'NJTruckCharging.dll')], {
  cwd: apiBin,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: API,
    ConnectionStrings__DefaultConnection: 'Server=(localdb)\\MSSQLLocalDB;Database=NJTruckCharging_DomainAuthTest;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True',
    Import__WorkbookPath: '',
    Auth__Mode: 'Supabase',
    Auth__Supabase__Url: AUTH,
    Auth__Supabase__PublishableKey: 'sb_publishable_e2e',
    Auth__Supabase__UserCheckSeconds: String(USER_CHECK_SECONDS),
    Auth__AccessRule: 'EmailDomain',
    Auth__DomainExceptionUserIds: ADMIN_ID,
    ReadOnlyDemo__Enabled: 'true',
    Cors__Origins__0: SITE,
  },
  stdio: ['ignore', 'pipe', 'pipe'],
});
let apiLog = '';
api.stdout.on('data', d => { apiLog += d; });
api.stderr.on('data', d => { apiLog += d; });
cleanup.push(() => { api.kill(); });
for (let i = 0; i < 120; i++) {
  try { if ((await fetch(API + '/healthz')).ok) break; } catch { /* starting */ }
  if (api.exitCode !== null) { console.error(apiLog); throw new Error('API exited'); }
  await sleep(1000);
}
const apiGet = async (path, token) => (await fetch(API + path, { headers: token ? { Authorization: `Bearer ${token}` } : {} })).status;

// ---------- 5. Browser scenarios ----------
const browser = await puppeteer.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: 'new', args: ['--no-sandbox'] });
cleanup.push(() => browser.close());

async function newPage() {
  const ctx = await browser.createBrowserContext();
  const page = await ctx.newPage();
  await page.setViewport({ width: 1280, height: 900 });
  page.apiStatuses = [];
  page.apiCalls = [];
  page.on('response', r => { if (r.url().startsWith(API + '/api/') && r.request().method() !== 'OPTIONS') { page.apiStatuses.push(r.status()); page.apiCalls.push(`${r.status()} ${r.url().slice(API.length)}`); } });
  return page;
}
const text = (page) => page.evaluate(() => document.body.innerText);
const waitText = (page, s, timeout = 15000) => page.waitForFunction(t => document.body.innerText.includes(t), { timeout }, s);
async function type(page, sel, value) {
  await page.$eval(sel, (el) => { el.value = ''; });
  await page.type(sel, value);
}
async function shot(page, name) { if (shots) await page.screenshot({ path: join(shots, name + '.png') }); }
const storedToken = (page) => page.evaluate(() => {
  const k = Object.keys(localStorage).find(k => k.startsWith('sb-') && k.endsWith('-auth-token'));
  return k ? JSON.parse(localStorage.getItem(k)).access_token : null;
});

async function createAccount(page, email) {
  await page.goto(SITE + '/', { waitUntil: 'networkidle0' });
  await waitText(page, 'Create account');
  await page.click('#si-tab-create');
  await type(page, '#si-email', email);
  await type(page, '#si-password', PASSWORD);
  await type(page, '#si-confirm', PASSWORD);
  await page.click('button[type="submit"]');
  await waitText(page, 'Check your e-mail');
}
async function signIn(page, email, password = PASSWORD) {
  if (!(await text(page)).includes('Sign in')) await page.goto(SITE + '/', { waitUntil: 'networkidle0' });
  await page.waitForSelector('#si-email');
  await type(page, '#si-email', email);
  await type(page, '#si-password', password);
  await page.click('button[type="submit"]');
}
const lastLink = async (email) => {
  const r = await fetch(`${AUTH}/__test/outbox?to=${encodeURIComponent(email)}`);
  return (await r.json()).at(-1)?.link;
};

try {
  // A/B. Both company domains: create → unconfirmed refused → open link → sign in → read-only dashboard.
  for (const email of ['e2e.pat@poweredison.com', 'E2E.Lee@EV-Edison.com']) {
    const page = await newPage();
    await createAccount(page, email);
    await shot(page, `check-email-${email.split('@')[1].toLowerCase()}`);
    check(true, `${email}: account created, "check your e-mail" shown`);

    await page.click('button.si-btn:not(.si-secondary)');                    // Back to sign in
    await signIn(page, email);
    await waitText(page, 'confirm your e-mail address first');
    check(true, `${email}: sign-in refused until the address is confirmed`);

    const link = await lastLink(email);
    check(!!link, `${email}: confirmation e-mail sent (outbox)`);
    await page.goto(link, { waitUntil: 'networkidle0' });
    await waitText(page, 'e-mail address is confirmed');
    check(!page.url().includes('code='), `${email}: confirmation code removed from the address bar (${page.url()})`);
    check((await storedToken(page)) === null, `${email}: the link itself did not sign anyone in`);

    const before = page.apiStatuses.length;
    await signIn(page, email);
    await page.waitForFunction(() => !document.querySelector('#si-email'), { timeout: 20000 });
    await waitText(page, 'Sign out');
    await sleep(2500);
    const calls = page.apiCalls.slice(before);
    check(calls.length > 0 && calls.every(c => c.startsWith('2')), `${email}: dashboard data loads (${calls.length} API calls: ${[...new Set(calls.map(c => c.split(' ')[0]))].join(',')})`);
    const nonOk = calls.filter(c => !c.startsWith('200'));
    if (nonOk.length) console.log('      non-200 but successful:', nonOk.join('; '));
    check((await text(page)).includes('Read-only demo'), `${email}: read-only badge shown`);
    await shot(page, `dashboard-${email.split('@')[1].toLowerCase()}`);

    const token = await storedToken(page);
    const write = await fetch(API + '/api/saved-sites', { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: '{"siteId":"TEST-0001"}' });
    check(write.status === 403 && (await write.json()).code === 'read_only', `${email}: writes refused (read_only)`);

    await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('button.signout')]);
    await page.waitForSelector('#si-email', { timeout: 15000 });
    check((await storedToken(page)) === null, `${email}: sign out clears the stored session`);
    // Sign-out revokes the session in Supabase; the API notices within its account-check cache time.
    await sleep((USER_CHECK_SECONDS + 1) * 1000);
    check(await apiGet('/api/sites?pageSize=1', token) === 401, `${email}: the signed-out token is refused by the API within ${USER_CHECK_SECONDS}s`);
    await page.browserContext().close();
  }

  // Expired / reused link.
  {
    const page = await newPage();
    const link = await lastLink('e2e.pat@poweredison.com');
    await page.goto(link, { waitUntil: 'networkidle0' });
    await waitText(page, 'no longer valid');
    check(!page.url().includes('#error'), 'a used confirmation link shows "no longer valid" and is cleaned from the address bar');
    await page.browserContext().close();
  }

  // C. Forbidden domains: the form stops them, and the real hook refuses them when the form is bypassed.
  {
    const page = await newPage();
    await page.goto(SITE + '/', { waitUntil: 'networkidle0' });
    await page.click('#si-tab-create');
    await type(page, '#si-email', 'someone@gmail.com');
    await type(page, '#si-password', PASSWORD);
    await type(page, '#si-confirm', PASSWORD);
    const disabled = await page.$eval('button[type="submit"]', b => b.disabled);
    check(disabled && (await text(page)).includes('work e-mail address'), 'form: gmail.com blocked with a work-address hint');
    await shot(page, 'create-forbidden-domain');
    await page.browserContext().close();

    const statsBefore = await (await fetch(AUTH + '/__test/stats')).json();
    for (const email of ['x@gmail.com', 'x@mail.poweredison.com', 'x@poweredison.com.evil.io', 'x@poweredison.co', 'x@poweredіson.com', 'x@ev-edison.com.', 'x@sub.ev-edison.com']) {
      const r = await authPost('/auth/v1/signup', { email, password: PASSWORD });
      check(r.status === 403 && r.body?.code === 'hook_refused', `hook (real SQL): sign-up for ${JSON.stringify(email)} refused`);
    }
    const statsAfter = await (await fetch(AUTH + '/__test/stats')).json();
    check(statsAfter.users === statsBefore.users, 'no account was created for any refused address');
  }

  // D. A personal-address account that exists anyway (created by an administrator before the hook) gets no data.
  {
    await authPost('/__test/create-confirmed', { email: 'old.personal@gmail.com', password: PASSWORD, bypassHook: true });
    const page = await newPage();
    await page.goto(SITE + '/', { waitUntil: 'networkidle0' });
    await signIn(page, 'old.personal@gmail.com');
    await waitText(page, 'does not have access');
    const t = await text(page);
    check(!/domain/i.test(t), 'personal-address account: generic "no access" screen, the rule is not explained');
    check(page.apiStatuses.includes(403) && !page.apiStatuses.includes(200), `personal-address account: API answers 403 only (${[...new Set(page.apiStatuses)]})`);
    await shot(page, 'no-access');
    await page.browserContext().close();
  }

  // E. Token forgery against the API.
  {
    const signIn1 = await authPost('/auth/v1/token?grant_type=password', { email: 'e2e.pat@poweredison.com', password: PASSWORD });
    const real = signIn1.body.access_token;
    check(await apiGet('/api/sites?pageSize=1', real) === 200, 'a real token works (baseline)');
    const [h, p] = real.split('.');
    const claims = JSON.parse(Buffer.from(p, 'base64url'));
    const forge = (hdr, payload, key) => {
      const hp = `${Buffer.from(JSON.stringify(hdr)).toString('base64url')}.${Buffer.from(JSON.stringify(payload)).toString('base64url')}`;
      return key ? `${hp}.${sign('sha256', Buffer.from(hp), { key, dsaEncoding: 'ieee-p1363' }).toString('base64url')}` : `${hp}.`;
    };
    const header = JSON.parse(Buffer.from(h, 'base64url'));
    const attacker = generateKeyPairSync('ec', { namedCurve: 'P-256' }).privateKey;
    check(await apiGet('/api/sites?pageSize=1', forge(header, claims, attacker)) === 401, 'forged: same kid, attacker key → 401');
    check(await apiGet('/api/sites?pageSize=1', forge({ alg: 'none', typ: 'JWT' }, claims)) === 401, 'forged: alg none → 401');
    check(await apiGet('/api/sites?pageSize=1', `${h}.${Buffer.from(JSON.stringify({ ...claims, email: 'boss@poweredison.com', exp: claims.exp + 86400 })).toString('base64url')}.${real.split('.')[2]}`) === 401,
      'forged: payload edited, original signature → 401');
    check(await apiGet('/api/sites?pageSize=1', forge(header, { ...claims, iss: 'http://127.0.0.1:1/auth/v1' }, attacker)) === 401, 'forged: other issuer → 401');
    const personal = await authPost('/auth/v1/token?grant_type=password', { email: 'old.personal@gmail.com', password: PASSWORD });
    const pc = JSON.parse(Buffer.from(personal.body.access_token.split('.')[1], 'base64url'));
    check(await apiGet('/api/sites?pageSize=1', forge(header, { ...pc, email: 'old.personal@poweredison.com' }, attacker)) === 401,
      'forged: personal account re-labelled as a company address → 401');
    const noSession = (await authPost('/__test/sign', { email: 'e2e.pat@poweredison.com' })).body.token;
    check(await apiGet('/api/sites?pageSize=1', noSession) === 401, 'validly signed token whose session does not exist → 401 (the API asks Supabase)');
    check(await apiGet('/api/sites?pageSize=1') === 401 && await apiGet('/api/sites/export') === 401, 'no token → 401 (data and CSV)');
  }

  // F. Unverified at the API: an account that loses its confirmation is refused even with a live session.
  {
    await authPost('/__test/create-confirmed', { email: 'e2e.unconfirm@poweredison.com', password: PASSWORD });
    const t = (await authPost('/auth/v1/token?grant_type=password', { email: 'e2e.unconfirm@poweredison.com', password: PASSWORD })).body.access_token;
    check(await apiGet('/api/sites?pageSize=1', t) === 200, 'confirmed account: 200');
    await authPost('/__test/unconfirm', { email: 'e2e.unconfirm@poweredison.com' });
    await sleep((USER_CHECK_SECONDS + 1) * 1000);
    check(await apiGet('/api/sites?pageSize=1', t) === 401, 'same session after the address is unconfirmed: 401');
  }

  // F2. Administrator exception: an existing Gmail administrator (created before the hook) keeps access by user id;
  //     every other personal account is still refused (scenario D); banning the administrator ends access.
  {
    await authPost('/__test/create-confirmed', { id: ADMIN_ID, email: ADMIN_EMAIL, password: PASSWORD, bypassHook: true });
    const page = await newPage();
    await page.goto(SITE + '/', { waitUntil: 'networkidle0' });
    await signIn(page, ADMIN_EMAIL);
    await waitText(page, 'Sign out');
    await sleep(2500);
    const calls = page.apiCalls;
    check(calls.length > 0 && calls.every(c => c.startsWith('2')), `administrator exception (${ADMIN_EMAIL}): dashboard loads (${calls.length} API calls)`);
    const token = await storedToken(page);
    check(await apiGet('/api/sites/export', token) === 200, 'administrator exception: CSV export works');
    const write = await fetch(API + '/api/saved-sites', { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: '{}' });
    check(write.status === 403 && (await write.json()).code === 'read_only', 'administrator exception: still read-only');
    const gmailSignup = await authPost('/auth/v1/signup', { email: 'c3.admin.personal2@gmail.com', password: PASSWORD });
    check(gmailSignup.status === 403, 'the hook still refuses new Gmail sign-ups (the exception is only on the API, by user id)');
    const other = (await authPost('/auth/v1/token?grant_type=password', { email: 'old.personal@gmail.com', password: PASSWORD })).body.access_token;
    check(await apiGet('/api/sites?pageSize=1', other) === 403, 'another confirmed Gmail account is still refused (403)');

    await authPost('/__test/ban', { email: ADMIN_EMAIL });
    const t0 = Date.now();
    let status = 200;
    while (status === 200 && Date.now() - t0 < (USER_CHECK_SECONDS + 3) * 1000) { await sleep(500); status = await apiGet('/api/sites?pageSize=1', token); }
    check(status === 401, `banned administrator: token refused after ${((Date.now() - t0) / 1000).toFixed(1)}s (limit ${USER_CHECK_SECONDS}s cache)`);
    await page.reload({ waitUntil: 'networkidle0' });
    await waitText(page, 'session has ended');
    await signIn(page, ADMIN_EMAIL);
    await waitText(page, 'disabled');
    check(true, 'banned administrator: dashboard returns to sign-in and signing in again is refused');
    await page.browserContext().close();
  }

  // G. Revocation: an employee leaves; the administrator deletes or bans the account while they are signed in.
  for (const action of ['delete', 'ban']) {
    const email = `e2e.leaver.${action}@poweredison.com`;
    await authPost('/__test/create-confirmed', { email, password: PASSWORD });
    const page = await newPage();
    await page.goto(SITE + '/', { waitUntil: 'networkidle0' });
    await signIn(page, email);
    await waitText(page, 'Sign out');
    await sleep(1500);
    const token = await storedToken(page);
    check(await apiGet('/api/sites?pageSize=1', token) === 200, `${action}: signed in and working`);
    await authPost(`/__test/${action}`, { email });
    const t0 = Date.now();
    let status = 200;
    while (status === 200 && Date.now() - t0 < (USER_CHECK_SECONDS + 3) * 1000) { await sleep(500); status = await apiGet('/api/sites?pageSize=1', token); }
    const secs = ((Date.now() - t0) / 1000).toFixed(1);
    check(status === 401, `${action}: the unexpired token is refused after ${secs}s (limit ${USER_CHECK_SECONDS}s cache)`);
    await page.reload({ waitUntil: 'networkidle0' });
    await waitText(page, 'session has ended');
    check(true, `${action}: the open dashboard returns to sign-in ("session has ended")`);
    await signIn(page, email);
    await waitText(page, action === 'ban' ? 'disabled' : 'not correct');
    check(true, `${action}: signing in again is refused`);
    if (action === 'delete') await shot(page, 'revoked-sign-in');
    await page.browserContext().close();
  }

  // H. Visual check of the sign-in and create-account screens (light and dark).
  if (shots) {
    const page = await newPage();
    for (const theme of ['light', 'dark']) {
      await page.goto(SITE + '/', { waitUntil: 'networkidle0' });
      await page.evaluate(t => { localStorage.setItem('njtc-theme', t); }, theme);
      await page.reload({ waitUntil: 'networkidle0' });
      await page.waitForSelector('#si-email');
      await shot(page, `sign-in-${theme}`);
      await page.click('#si-tab-create');
      await type(page, '#si-email', 'new.hire@poweredison.com');
      await type(page, '#si-password', 'short');
      await shot(page, `create-account-${theme}`);
    }
    await page.setViewport({ width: 390, height: 844 });
    await page.reload({ waitUntil: 'networkidle0' });
    await page.click('#si-tab-create');
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    check(!overflow, 'phone width (390px): no horizontal scrolling');
    await shot(page, 'create-account-phone');
    await page.browserContext().close();
  }
} catch (e) {
  failures++;
  console.error('FAIL  (exception)', e);
} finally {
  for (const c of cleanup.reverse()) { try { await c(); } catch { /* ignore */ } }
}
console.log(`\n${passes} passed, ${failures} failed`);
if (failures) console.log('--- API log (tail) ---\n' + apiLog.slice(-3000));
process.exit(failures ? 1 : 0);
