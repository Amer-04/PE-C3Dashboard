// Real-browser test of methodology editing across ORIGINS, like production (dashboard on netlify.app, API on
// onrender.com): the dashboard is served from http://localhost:4320 and the API from http://127.0.0.1:5203, so Chrome
// itself sends "Origin: http://localhost:4320" and "Sec-Fetch-Site: cross-site". A third origin (http://[::1]:4321)
// plays a malicious site. Never contacts Supabase, Render or Netlify.
//
// Usage: node e2e/methodology-editing/run-browser-origin-e2e.mjs <deps with puppeteer-core> <web build dir> <API bin dir> "<SQL Server connection string>"
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { extname, join, resolve } from 'node:path';
import { startMockSupabaseAuth } from '../self-registration/mock-supabase-auth.mjs';

const [depsArg, webArg, apiBinArg, dbArg] = process.argv.slice(2);
const require = createRequire(join(resolve(depsArg), 'package.json'));
const puppeteer = require('puppeteer-core');
const web = resolve(webArg, 'browser');
const apiBin = resolve(apiBinArg);
const PORTS = { auth: 54351, api: 5203, web: 4320, evil: 4321 };
const SITE = `http://localhost:${PORTS.web}`, EVIL = `http://[::1]:${PORTS.evil}`;
const API = `http://127.0.0.1:${PORTS.api}`, AUTH = `http://127.0.0.1:${PORTS.auth}`;
const PASSWORD = 'Correct-Horse-Battery-42';
const EDITOR = 'rules.editor@poweredison.com';

let failures = 0, passes = 0;
const check = (ok, what) => { ok ? passes++ : failures++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}`); };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const cleanup = [];

try {
  const auth = await startMockSupabaseAuth({ port: PORTS.auth, hook: async () => ({}), siteUrl: SITE + '/' });
  cleanup.push(() => auth.close());
  const post = (p, b) => fetch(AUTH + p, { method: 'POST', headers: { 'Content-Type': 'application/json', apikey: 'sb_publishable_e2e' }, body: JSON.stringify(b) });
  await post('/__test/create-confirmed', { email: EDITOR, password: PASSWORD });

  const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml', '.ico': 'image/x-icon', '.png': 'image/png' };
  const dash = createServer((req, res) => {
    const p = new URL(req.url, SITE).pathname;
    if (p === '/app-config.json') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ apiBaseUrl: API, supabaseUrl: AUTH, supabasePublishableKey: 'sb_publishable_e2e', readOnly: true, selfRegistration: false })); }
    let f = join(web, decodeURIComponent(p)); if (!f.startsWith(web) || !existsSync(f) || p === '/') f = join(web, 'index.html');
    res.writeHead(200, { 'Content-Type': types[extname(f)] ?? 'application/octet-stream' }); res.end(readFileSync(f));
  });
  await new Promise(r => dash.listen(PORTS.web, 'localhost', r)); cleanup.push(() => new Promise(r => dash.close(r)));
  const evil = createServer((req, res) => { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<!doctype html><title>other site</title>'); });
  await new Promise(r => evil.listen(PORTS.evil, '::1', r)); cleanup.push(() => new Promise(r => evil.close(r)));

  const api = spawn('dotnet', [join(apiBin, 'NJTruckCharging.dll')], {
    cwd: apiBin, stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: API, ConnectionStrings__DefaultConnection: dbArg, Import__WorkbookPath: '',
      Auth__Mode: 'Supabase', Auth__Supabase__Url: AUTH, Auth__Supabase__PublishableKey: 'sb_publishable_e2e', Auth__Supabase__UserCheckSeconds: '0',
      Auth__AccessRule: 'EmailDomain', ReadOnlyDemo__Enabled: 'true', Methodology__CloudEditing__Enabled: 'true', Cors__Origins__0: SITE },
  });
  let apiLog = ''; api.stdout.on('data', d => { apiLog += d; }); api.stderr.on('data', d => { apiLog += d; });
  cleanup.push(() => api.kill());
  for (let i = 0; i < 120; i++) { try { if ((await fetch(API + '/healthz')).ok) break; } catch { /* starting */ } if (api.exitCode !== null) { console.log(apiLog); throw new Error('API exited'); } await sleep(1000); }

  const browser = await puppeteer.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: 'new', args: ['--no-sandbox'] });
  cleanup.push(() => browser.close());
  const page = await browser.newPage();
  // The headers Chrome really puts on the wire (Origin, Sec-Fetch-Site are added by the network stack).
  const cdp = await page.target().createCDPSession();
  await cdp.send('Network.enable');
  const urls = new Map(), seen = [];
  cdp.on('Network.requestWillBeSent', e => urls.set(e.requestId, `${e.request.method} ${e.request.url}`));
  cdp.on('Network.requestWillBeSentExtraInfo', e => {
    const u = urls.get(e.requestId) ?? '';
    if (/^(POST|PUT|DELETE) .*\/api\/methodology\//.test(u)) seen.push({ u, origin: e.headers.origin ?? e.headers.Origin, site: e.headers['sec-fetch-site'] ?? e.headers['Sec-Fetch-Site'] });
  });

  // Sign in through the real dashboard.
  await page.goto(SITE + '/', { waitUntil: 'networkidle0' });
  await page.waitForSelector('#si-email');
  await page.type('#si-email', EDITOR); await page.type('#si-password', PASSWORD);
  await page.click('button[type="submit"]');
  await page.waitForFunction(() => !document.querySelector('#si-email'), { timeout: 20000 });
  const token = await page.evaluate(() => { const k = Object.keys(localStorage).find(k => k.startsWith('sb-') && k.endsWith('-auth-token')); return k ? JSON.parse(localStorage.getItem(k)).access_token : null; });
  check(!!token, 'signed in through the dashboard');

  // Make sure "Edit Rule" must CREATE a draft (no reusable draft left from earlier runs). Done outside the browser
  // (Node sends no Origin/Sec-Fetch-Site), so it works on every build.
  const nodeCall = (m, p) => fetch(API + p, { method: m, headers: { Authorization: `Bearer ${token}` } });
  const vs = await (await nodeCall('GET', '/api/methodology/versions')).json();
  for (const v of vs.filter(v => v.status === 'Draft')) await nodeCall('DELETE', `/api/methodology/drafts/${v.id}?revision=${v.revision}`);
  check((await (await nodeCall('GET', '/api/methodology/versions')).json()).every(v => v.status !== 'Draft'), 'no drafts left: Edit Rule has to create one');

  // The UI: Methodology page offers editing, and "Edit Rule" opens a draft (POST /api/methodology/drafts, cross-site).
  await page.goto(SITE + '/methodology', { waitUntil: 'networkidle0' });
  await page.waitForFunction(() => document.body.innerText.includes('Site Scoring Rules'), { timeout: 20000 });
  check(await page.evaluate(() => [...document.querySelectorAll('button')].some(b => b.textContent.includes('Add Rule'))), 'Methodology page offers editing (Add Rule visible)');
  await page.click('.rl-edit');
  const outcome = await Promise.race([
    page.waitForFunction(() => location.pathname.startsWith('/methodology/versions/'), { timeout: 20000 }).then(() => 'opened'),
    page.waitForFunction(() => document.body.innerText.includes('Cross-site methodology changes are not allowed'), { timeout: 20000 }).then(() => 'cross-site refusal'),
  ]).catch(() => 'timeout');
  check(outcome === 'opened', `"Edit Rule" from the dashboard opens a draft workspace (got: ${outcome})`);
  const h = seen.find(x => x.u.startsWith('POST') && x.u.includes('/api/methodology/drafts')) ?? {};
  console.log(`      Chrome sent: ${h.u ?? '(no POST seen)'}  Origin=${h.origin}  Sec-Fetch-Site=${h.site}`);
  check(h.origin === SITE && h.site === 'cross-site', 'the browser marked the dashboard request cross-site with the exact dashboard origin (like production)');

  // API calls exactly as the dashboard makes them (same page, same origin, same headers).
  const call = (method, path, body, withToken = true) => page.evaluate(async (a) => {
    const r = await fetch(a.api + a.path, { method: a.method, headers: { 'Content-Type': 'application/json', ...(a.token ? { Authorization: `Bearer ${a.token}` } : {}) }, body: a.body ? JSON.stringify(a.body) : undefined });
    let json = null; try { json = await r.json(); } catch { /* empty */ }
    return { status: r.status, json };
  }, { api: API, method, path, body, token: withToken ? token : null });

  const scoresBefore = JSON.stringify((await call('GET', '/api/sites?pageSize=25&sortBy=Score&descending=true')).json?.items?.map(s => [s.siteId, s.score, s.rank]));
  const d = await call('POST', '/api/methodology/drafts', { name: 'browser cross-origin test' });
  check(d.status === 200, `create draft from the dashboard origin → ${d.status} ${d.json?.title ?? ''}`);
  if (d.status === 200) {
    const id = d.json.version.id, rules = d.json.rules;
    rules.criteria.find(c => c.key === 'port').defaultPoints = 3;
    const s1 = await call('PUT', `/api/methodology/drafts/${id}`, { name: 'browser cross-origin test', rules, expectedRevision: 1 });
    check(s1.status === 200 && s1.json.version.revision === 2, `save draft → ${s1.status}, revision ${s1.json?.version?.revision}`);
    const s2 = await call('PUT', `/api/methodology/drafts/${id}`, { name: 'stale', rules, expectedRevision: 1 });
    check(s2.status === 409 && s2.json?.code === 'draft_conflict', `stale save → ${s2.status} ${s2.json?.code}`);
    const scoresAfter = JSON.stringify((await call('GET', '/api/sites?pageSize=25&sortBy=Score&descending=true')).json?.items?.map(s => [s.siteId, s.score, s.rank]));
    check(scoresAfter === scoresBefore, 'saving a draft did not change live scores or ranks');
    const log = (await call('GET', `/api/methodology/changes?versionId=${id}`)).json?.entries ?? [];
    check(log.length >= 2 && log.every(e => e.actorEmail === EDITOR && /^[0-9a-f-]{36}$/.test(e.actorUserId)), `audited: ${log.map(e => e.action).join(' ← ')} by ${EDITOR} with user id`);
    check((await call('DELETE', `/api/methodology/drafts/${id}?revision=2`)).status === 204, 'delete the test draft');
  }
  check((await call('POST', '/api/methodology/drafts', { name: 'x' }, false)).status === 401, 'without a token → 401 (the trusted origin is not a credential)');
  const sv = await call('POST', '/api/saved-sites', { siteId: 'TEST-0001' });
  check(sv.status === 403 && sv.json?.code === 'read_only', `other writes from the dashboard origin stay read-only → ${sv.status} ${sv.json?.code}`);

  // A different site with a stolen token: the browser's CORS preflight is refused, so the request never reaches the API.
  const evilPage = await browser.newPage();
  await evilPage.goto(EVIL + '/', { waitUntil: 'load' });
  const ev = await evilPage.evaluate(async (a) => {
    try { const r = await fetch(a.api + '/api/methodology/drafts', { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${a.token}` }, body: '{"name":"evil"}' }); return 'status ' + r.status; }
    catch (e) { return 'blocked: ' + e.message; }
  }, { api: API, token });
  check(ev.startsWith('blocked'), `request from another site with a valid token → ${ev}`);
  const evDrafts = (await call('GET', '/api/methodology/versions')).json?.filter(v => v.name === 'evil').length ?? -1;
  check(evDrafts === 0, 'no draft was created by the other site');
} catch (e) {
  failures++; console.error('FAIL  unexpected error:', e);
} finally {
  for (const f of cleanup.reverse()) { try { await f(); } catch { /* ignore */ } }
}
console.log(`\n${passes} passed, ${failures} failed`);
process.exit(failures ? 1 : 0);
