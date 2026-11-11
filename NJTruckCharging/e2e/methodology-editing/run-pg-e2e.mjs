// End-to-end test of SHARED METHODOLOGY EDITING against a real PostgreSQL with the real C3 roles. Never contacts
// Supabase, Render or Netlify: everything runs on this PC.
//
//   throw-away PostgreSQL (54339)  built EXACTLY like production today: sql/01 roles, schema c3 + data (CloudCopy
//                                  create-schema + copy), the audit columns removed again (= the deployed schema),
//                                  sql/03 grants, sql/04 lockdown. Then the new migration: sql/09 (writer role, as
//                                  postgres), CloudCopy "methodology-editing" (sql/10, as c3_migrator), sql/04, sql/05.
//   mock Supabase Auth (54331)     ES256 tokens, JWKS, /user (reused from e2e/self-registration)
//   the real API, Release (5183)   PostgreSQL mode: read-only login for everything, writer login for methodology only,
//                                  ReadOnlyDemo on, Methodology:CloudEditing on, AccessRule=EmailDomain + Gmail exception
//
// Usage (deps = a folder with: npm i pg embedded-postgres):
//   dotnet build NJTruckCharging/NJTruckCharging.csproj -c Release; dotnet build NJTruckCharging.CloudCopy
//   node e2e/methodology-editing/run-pg-e2e.mjs <deps> "<SQL Server connection string of a migrated test database>"
import { createRequire } from 'node:module';
import { spawn, spawnSync } from 'node:child_process';
import { readFileSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { startMockSupabaseAuth } from '../self-registration/mock-supabase-auth.mjs';

const [depsArg, sourceArg] = process.argv.slice(2);
if (!depsArg || !sourceArg) { console.error('usage: run-pg-e2e.mjs <deps> <source SQL Server connection string>'); process.exit(2); }
const deps = resolve(depsArg);
const require = createRequire(join(deps, 'package.json'));
const { default: EmbeddedPostgres } = await import(pathToFileURL(require.resolve('embedded-postgres')).href);
const pg = require('pg');

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, '../..');
const sqlDir = join(repo, 'NJTruckCharging.CloudCopy/sql');
const sql = (f) => readFileSync(join(sqlDir, f), 'utf8');
const PORTS = { pg: 54339, auth: 54331, api: 5183 };
const API = `http://127.0.0.1:${PORTS.api}`;
const AUTH = `http://127.0.0.1:${PORTS.auth}`;
const PW = { admin: 'local-test-only', migrator: 'migrator-local-test-0123456789', readonly: 'readonly-local-test-0123456789', writer: 'writer-local-test-0123456789' };
const conn = (user, pw) => `Host=127.0.0.1;Port=${PORTS.pg};Database=postgres;Username=${user};Password=${pw};Pooling=false`;

const EMPLOYEE = 'rules.editor@poweredison.com', COLLEAGUE = 'second.editor@ev-edison.com', OUTSIDER = 'someone.else@gmail.com';
const ADMIN_ID = 'a11ce000-0000-4000-8000-00000000ad01', ADMIN_EMAIL = 'c3.admin.personal@gmail.com';
const PASSWORD = 'Correct-Horse-Battery-42';

let failures = 0, passes = 0;
const check = (ok, what) => { ok ? passes++ : failures++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}`); };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const cleanup = [];

async function client(user, pw) {
  const c = new pg.Client({ host: '127.0.0.1', port: PORTS.pg, user, password: pw, database: 'postgres' });
  await c.connect();
  return c;
}
async function refused(c, statement, what, code) {
  try { await c.query('BEGIN'); await c.query(statement); await c.query('ROLLBACK'); check(false, `${what} (was allowed!)`); }
  catch (e) { await c.query('ROLLBACK').catch(() => {}); check(!code || e.code === code, `${what} → refused (${e.code} ${String(e.message).slice(0, 70)})`); }
}
function cloudCopy(command, target) {
  const dll = join(repo, 'NJTruckCharging.CloudCopy/bin/Debug/net10.0/NJTruckCharging.CloudCopy.dll');
  const r = spawnSync('dotnet', [dll, command], { env: { ...process.env, C3_SOURCE: sourceArg, C3_TARGET: target }, encoding: 'utf8' });
  if (r.status !== 0) throw new Error(`CloudCopy ${command} failed:\n${r.stdout}\n${r.stderr}`);
  return r.stdout;
}

try {
  // ---------- 1. PostgreSQL shaped like production today ----------
  const pgDir = join(deps, 'methodology-e2e-data');
  rmSync(pgDir, { recursive: true, force: true });
  const pgServer = new EmbeddedPostgres({ databaseDir: pgDir, user: 'postgres', password: PW.admin, port: PORTS.pg,
    persistent: false, initdbFlags: ['--encoding=UTF8', '--locale=C'], onLog: () => {}, onError: () => {} });
  await pgServer.initialise();
  await pgServer.start();
  cleanup.push(() => pgServer.stop());
  const admin = await client('postgres', PW.admin);
  cleanup.push(() => admin.end());
  for (const r of ['anon', 'authenticated', 'service_role', 'authenticator']) await admin.query(`CREATE ROLE ${r} NOLOGIN`);
  await admin.query(sql('01-roles-and-schema.sql').replaceAll('{{MIGRATOR_VERIFIER}}', PW.migrator).replaceAll('{{READONLY_VERIFIER}}', PW.readonly));
  const migratorConn = `Host=127.0.0.1;Port=${PORTS.pg};Database=postgres;Username=c3_migrator;Password=${PW.migrator}`;
  cloudCopy('create-schema', migratorConn);
  console.log(cloudCopy('copy', migratorConn).trim().split('\n').slice(-2).join('\n'));
  {
    // Populate every user-data table (the source test database keeps them empty), so "unchanged" is meaningful.
    await admin.query(`SET search_path = c3;
      INSERT INTO "SavedSites" ("SiteId","Notes","CreatedAtUtc","UpdatedAtUtc") SELECT "SiteId",'regression bookmark',now(),now() FROM "CandidateSites" ORDER BY "Score" DESC LIMIT 25;
      INSERT INTO "SiteContacts" ("SiteId","Name","ContactType","VerificationStatus","SourceUrl","CreatedAtUtc","UpdatedAtUtc")
        SELECT "SiteId",'Owner '||"SiteId",'PropertyOwner','Unverified','https://example.org/'||"SiteId",now(),now() FROM "CandidateSites" ORDER BY "Score" DESC LIMIT 20;
      INSERT INTO "FeasibilityItems" ("SiteId","ItemKey","Status","Evidence","CreatedAtUtc","UpdatedAtUtc")
        SELECT "SiteId",'Zoning','Verified','zoning letter',now(),now() FROM "CandidateSites" ORDER BY "Score" DESC LIMIT 20;
      INSERT INTO "SiteScreenings" ("SiteId","Decision","Evidence","CreatedAtUtc","UpdatedAtUtc") SELECT "SiteId",'Promising','site visit',now(),now() FROM "CandidateSites" ORDER BY "Score" DESC LIMIT 15;
      INSERT INTO "SiteScreeningEntries" ("SiteId","Decision","Evidence","RecordedAtUtc") SELECT "SiteId",'Promising','site visit',now() FROM "CandidateSites" ORDER BY "Score" DESC LIMIT 15;
      INSERT INTO "ResearchHistoryEntries" ("SiteId","Subject","Action","Note","RecordedAtUtc") SELECT "SiteId",'Site','Started','research',now() FROM "CandidateSites" ORDER BY "Score" DESC LIMIT 15;
      INSERT INTO "CustomDataFields" ("Key","Label","Type","Source","Categories","CreatedAtUtc","UpdatedAtUtc") VALUES ('cfZoning','Zoning','Category','Manual','Permitted,Conditional',now(),now());
      INSERT INTO "CustomFieldValues" ("FieldKey","SiteId","CategoryValue","Evidence","Source","CreatedAtUtc","UpdatedAtUtc")
        SELECT 'cfZoning',"SiteId",'Permitted','zoning map','Manual',now(),now() FROM "CandidateSites" ORDER BY "Score" DESC LIMIT 10;
      INSERT INTO "NearbyFleets" ("CandidateSiteId","Segment","RankInSegment","Carrier","Trucks","Tractors","Drivers","ApproxMiles")
        SELECT "Id","Segment","RankInSegment",'Carrier '||"Id",12,8,15,1.5 FROM "CandidateSites" ORDER BY "Score" DESC LIMIT 10;
      SET search_path = DEFAULT;`);
    // The deployed schema predates this change: remove the audit columns/table again (the rollback's optional block).
    const m = await client('c3_migrator', PW.migrator);
    const block = sql('10-methodology-editing-rollback.sql').split('---- OPTIONAL')[1].split('\n').filter(l => l.startsWith('-- ') && !l.startsWith('-- ----'))
      .map(l => l.slice(3)).join('\n');
    await m.query(block);
    await m.end();
  }
  cloudCopy('secure', migratorConn);
  await admin.query(sql('04-lockdown.sql'));
  check((await admin.query(`SELECT count(*)::int n FROM information_schema.columns WHERE table_schema='c3' AND table_name='MethodologyVersions' AND column_name='Revision'`)).rows[0].n === 0,
    'starting point = today\'s production schema (no audit columns)');

  // Fingerprint of every pre-existing row (only the columns that exist today).
  async function fingerprint() {
    const tables = (await admin.query(`SELECT tablename FROM pg_tables WHERE schemaname='c3' AND tablename <> 'MethodologyChangeLog' ORDER BY 1`)).rows.map(r => r.tablename);
    const out = {};
    for (const t of tables) {
      const cols = (await admin.query(`SELECT column_name FROM information_schema.columns WHERE table_schema='c3' AND table_name=$1
        AND column_name NOT IN ('Revision','CreatedByUserId','CreatedByEmail','UpdatedByUserId','UpdatedByEmail','PublishedByUserId','PublishedByEmail','ActorUserId','ActorEmail')
        ORDER BY ordinal_position`, [t])).rows.map(r => `"${r.column_name}"`);
      out[t] = (await admin.query(`SELECT count(*)::int n, md5(coalesce(string_agg(x, '|' ORDER BY x), '')) h FROM (SELECT row(${cols.join(',')})::text x FROM c3."${t}") s`)).rows[0];
    }
    return out;
  }
  const before = await fingerprint();
  const scoreCols = (await admin.query(`SELECT column_name FROM information_schema.columns WHERE table_schema='c3' AND table_name='CandidateSites'
     AND NOT (column_name IN ('Score','ComputedRank','TruckDemandPoints','ScoreVersionId') OR column_name LIKE 'Breakdown\\_%') ORDER BY ordinal_position`)).rows.map(r => `"${r.column_name}"`);
  const siteDataHash = async () => (await admin.query(`SELECT md5(string_agg(x, '|' ORDER BY x)) h FROM (SELECT row(${scoreCols.join(',')})::text x FROM c3."CandidateSites") s`)).rows[0].h;
  const siteDataBefore = await siteDataHash();
  const userTables = ['SavedSites', 'SiteContacts', 'FeasibilityItems', 'SiteScreenings', 'SiteScreeningEntries', 'ResearchHistoryEntries', 'CustomDataFields', 'CustomFieldValues', 'NearbyFleets'];
  console.log(`  ${before.CandidateSites.n} sites, ${before.MethodologyVersions.n} versions; populated: ` + userTables.map(t => `${t} ${before[t].n}`).join(', '));
  check(userTables.every(t => before[t].n > 0), 'every user-data table is populated before the migration');

  // ---------- 2. The migration, exactly as documented ----------
  await admin.query(sql('09-methodology-writer-role.sql').replaceAll('{{WRITER_VERIFIER}}', PW.writer));
  console.log('  ' + cloudCopy('methodology-editing', migratorConn).trim());
  await admin.query(sql('04-lockdown.sql'));
  const after = await fingerprint();
  check(Object.keys(before).every(t => before[t].n === after[t].n && before[t].h === after[t].h),
    `migration kept every row of all ${Object.keys(before).length} existing tables unchanged (counts + content hashes)`);
  const isolation = (await admin.query(sql('05-check-isolation.sql'))).rows;
  const bad = isolation.filter(r => r.result !== 'OK');
  check(bad.length === 0, `05-check-isolation: ${isolation.length} checks OK${bad.length ? ' — FAIL: ' + bad.map(r => r.check_name + ' ' + r.detail).join('; ') : ''}`);
  // Re-running the migration converges (idempotent).
  await admin.query(`ALTER ROLE c3_migrator LOGIN; GRANT USAGE, CREATE ON SCHEMA c3 TO c3_migrator;`);
  cloudCopy('methodology-editing', migratorConn);
  await admin.query(sql('04-lockdown.sql'));
  check((await admin.query(sql('05-check-isolation.sql'))).rows.every(r => r.result === 'OK'), 'migration re-run is idempotent (isolation still all OK)');

  // ---------- 3a. The writer's EXACT privileges (nothing more than listed) ----------
  {
    const q = async (text) => (await admin.query(text)).rows.map(r => Object.values(r).join(' ')).sort();
    const tablePrivs = await q(`SELECT c.relname, a.privilege_type FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace,
        aclexplode(c.relacl) a WHERE n.nspname = 'c3' AND a.grantee = 'c3_methodology_writer'::regrole`);
    const expectedTable = ['CandidateSites SELECT', 'MethodologyActivations INSERT', 'MethodologyActivations SELECT', 'MethodologyChangeLog INSERT',
      'MethodologyChangeLog SELECT', 'MethodologyState SELECT', 'MethodologyVersions DELETE', 'MethodologyVersions INSERT', 'MethodologyVersions SELECT',
      'SiteVersionScores DELETE', 'SiteVersionScores INSERT', 'SiteVersionScores SELECT'].sort();
    check(JSON.stringify(tablePrivs) === JSON.stringify(expectedTable), `writer table privileges are exactly the 12 expected: ${tablePrivs.join(', ')}`);
    const colPrivs = await q(`SELECT c.relname, at.attname, a.privilege_type FROM pg_attribute at JOIN pg_class c ON c.oid = at.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace, aclexplode(at.attacl) a WHERE n.nspname = 'c3' AND a.grantee = 'c3_methodology_writer'::regrole`);
    const breakdown = (await admin.query(`SELECT column_name FROM information_schema.columns WHERE table_schema = 'c3' AND table_name = 'CandidateSites' AND column_name LIKE 'Breakdown%'`)).rows.map(r => r.column_name);
    const expectedCols = [
      ...['Score', 'ComputedRank', 'TruckDemandPoints', 'ScoreVersionId', ...breakdown].map(x => `CandidateSites ${x} UPDATE`),
      ...['ActiveVersionId', 'PreviousActiveVersionId', 'ActivatedAtUtc'].map(x => `MethodologyState ${x} UPDATE`),
      ...['Name', 'Status', 'RulesJson', 'RulesHash', 'ChangeSummary', 'UpdatedAtUtc', 'PublishedAtUtc', 'PreviewRulesHash', 'ScoresDataVersion', 'PreviewedAtUtc',
          'PreviewSummaryJson', 'Revision', 'UpdatedByUserId', 'UpdatedByEmail', 'PublishedByUserId', 'PublishedByEmail'].map(x => `MethodologyVersions ${x} UPDATE`),
    ].sort();
    check(JSON.stringify(colPrivs) === JSON.stringify(expectedCols),
      `writer column privileges are exactly the ${expectedCols.length} expected UPDATE columns (${breakdown.length} Breakdown_*; got ${colPrivs.length})`);
    const other = (await admin.query(`SELECT
        (SELECT count(*) FROM pg_class c, aclexplode(c.relacl) a WHERE c.relkind = 'S' AND a.grantee = 'c3_methodology_writer'::regrole)::int AS sequences,
        (SELECT count(*) FROM pg_proc p, aclexplode(p.proacl) a WHERE a.grantee = 'c3_methodology_writer'::regrole)::int AS functions,
        (SELECT count(*) FROM pg_auth_members WHERE member = 'c3_methodology_writer'::regrole)::int AS memberships,
        (SELECT count(*) FROM pg_namespace n, aclexplode(n.nspacl) a WHERE a.grantee = 'c3_methodology_writer'::regrole
           AND NOT (n.nspname = 'c3' AND a.privilege_type = 'USAGE'))::int AS schema_extra,
        (SELECT count(*) FROM pg_class c WHERE c.relowner = 'c3_methodology_writer'::regrole)::int AS owned,
        (SELECT rolsuper OR rolcreaterole OR rolcreatedb OR rolbypassrls OR rolreplication OR rolinherit FROM pg_roles WHERE rolname = 'c3_methodology_writer') AS powerful,
        (SELECT rolconnlimit FROM pg_roles WHERE rolname = 'c3_methodology_writer')::int AS connlimit`)).rows[0];
    check(other.sequences === 0 && other.functions === 0 && other.memberships === 0 && other.schema_extra === 0 && other.owned === 0 && !other.powerful && other.connlimit === 3,
      `writer: no sequence/function/role-membership/extra-schema privileges, owns nothing, no special attributes, 3 connections ${JSON.stringify(other)}`);
  }

  // ---------- 3. The writer role, probed directly ----------
  const w = await client('c3_methodology_writer', PW.writer);
  cleanup.push(() => w.end());
  const baselineId = (await w.query(`SELECT "Id" FROM c3."MethodologyVersions" WHERE "IsBaseline"`)).rows[0].Id;
  await refused(w, `UPDATE c3."CandidateSites" SET "SiteAddress" = 'x' WHERE "Id" = (SELECT min("Id") FROM c3."CandidateSites")`, 'writer cannot change site data (address)', '42501');
  await refused(w, `UPDATE c3."CandidateSites" SET "OpenAcresEst" = 0`, 'writer cannot change site data (acreage)', '42501');
  await refused(w, `DELETE FROM c3."CandidateSites"`, 'writer cannot delete sites', '42501');
  await refused(w, `INSERT INTO c3."SavedSites" ("SiteId") VALUES ('X')`, 'writer cannot write saved sites', '42501');
  await refused(w, `SELECT * FROM c3."SiteContacts" LIMIT 1`, 'writer cannot even read contacts', '42501');
  await refused(w, `UPDATE c3."SiteScreenings" SET "Decision" = 'x'`, 'writer cannot change screening', '42501');
  await refused(w, `UPDATE c3."MethodologyActivations" SET "Note" = 'x'`, 'writer cannot rewrite activation history', '42501');
  await refused(w, `DELETE FROM c3."MethodologyChangeLog"`, 'writer cannot delete the audit trail', '42501');
  await refused(w, `UPDATE c3."MethodologyVersions" SET "RulesJson" = '{}' WHERE "Id" = ${baselineId}`, 'writer cannot change the baseline rules (trigger)', '42501');
  await refused(w, `UPDATE c3."MethodologyVersions" SET "IsBaseline" = false WHERE "Id" = ${baselineId}`, 'writer cannot change the baseline flag', '42501');
  await refused(w, `UPDATE c3."MethodologyState" SET "DataVersion" = 99`, 'writer cannot change the data version', '42501');
  await refused(w, `TRUNCATE c3."SiteVersionScores"`, 'writer cannot truncate', '42501');
  await refused(w, `CREATE TABLE c3.evil (x int)`, 'writer cannot create tables', '42501');
  await refused(w, `INSERT INTO c3."MethodologyVersions" ("Name","Status","IsBaseline","RulesJson","RulesHash","CreatedAtUtc","UpdatedAtUtc","Revision") VALUES ('x','Published',false,'{}','h',now(),now(),1)`,
    'writer cannot insert a published version', '42501');
  const del = await w.query(`DELETE FROM c3."MethodologyVersions" WHERE "Id" = ${baselineId}`).then(r => r.rowCount, e => e.code);
  check(del === 0 || del === '42501', `writer cannot delete the baseline (row-level security → ${del})`);
  const ro = await client('c3_runtime_readonly', PW.readonly);
  cleanup.push(() => ro.end());
  await refused(ro, `INSERT INTO c3."MethodologyChangeLog" ("VersionId","VersionName","Action","AtUtc") VALUES (1,'x','SaveDraft',now())`, 'read-only login still cannot write (audit table)');
  await refused(ro, `UPDATE c3."MethodologyVersions" SET "Name" = 'x'`, 'read-only login still cannot write (versions)');

  // ---------- 4. Mock Supabase Auth + the real API on the two logins ----------
  const auth = await startMockSupabaseAuth({ port: PORTS.auth, hook: async () => ({}), siteUrl: 'http://localhost/' });
  cleanup.push(() => auth.close());
  const authPost = async (path, body) => {
    const r = await fetch(AUTH + path, { method: 'POST', headers: { 'Content-Type': 'application/json', apikey: 'sb_publishable_e2e' }, body: JSON.stringify(body) });
    return { status: r.status, body: await r.json().catch(() => null) };
  };
  for (const email of [EMPLOYEE, COLLEAGUE, OUTSIDER]) await authPost('/__test/create-confirmed', { email, password: PASSWORD });
  await authPost('/__test/create-confirmed', { id: ADMIN_ID, email: ADMIN_EMAIL, password: PASSWORD });
  const signIn = async (email) => (await authPost('/auth/v1/token?grant_type=password', { email, password: PASSWORD })).body.access_token;

  let api, apiLog = '';
  async function startApi(extra) {
    const apiBin = join(repo, 'NJTruckCharging/bin/Release/net10.0');
    api = spawn('dotnet', [join(apiBin, 'NJTruckCharging.dll')], {
      cwd: apiBin,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: API,
        Database__Provider: 'Postgres', Database__SkipInitialization: 'true',
        ConnectionStrings__DefaultConnection: conn('c3_runtime_readonly', PW.readonly),
        Import__WorkbookPath: '',
        Auth__Mode: 'Supabase', Auth__Supabase__Url: AUTH, Auth__Supabase__PublishableKey: 'sb_publishable_e2e', Auth__Supabase__UserCheckSeconds: '0',
        Auth__AccessRule: 'EmailDomain', Auth__DomainExceptionUserIds: ADMIN_ID,
        ReadOnlyDemo__Enabled: 'true', Cors__Origins__0: 'https://c3dashboard.example',
        ...extra,
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    apiLog = '';
    api.stdout.on('data', d => { apiLog += d; });
    api.stderr.on('data', d => { apiLog += d; });
    for (let i = 0; i < 120; i++) {
      try { if ((await fetch(API + '/healthz')).ok) return; } catch { /* starting */ }
      if (api.exitCode !== null) { console.error(apiLog); throw new Error('API exited'); }
      await sleep(1000);
    }
    throw new Error('API did not start');
  }
  const stopApi = async () => { api.kill(); await new Promise(r => api.once('exit', r)); };
  cleanup.push(() => api?.exitCode === null && api.kill());
  const call = async (method, path, token, body) => {
    const r = await fetch(API + path, { method, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) },
      body: body ? JSON.stringify(body) : undefined });
    const text = await r.text();
    let json = null; try { json = JSON.parse(text); } catch { /* not json */ }
    return { status: r.status, json, text, headers: r.headers };
  };

  await startApi({ Methodology__CloudEditing__Enabled: 'true', ConnectionStrings__MethodologyWriter: conn('c3_methodology_writer', PW.writer) });
  const emp = await signIn(EMPLOYEE), col = await signIn(COLLEAGUE), out = await signIn(OUTSIDER), adm = await signIn(ADMIN_EMAIL);

  // Who may edit.
  check((await call('POST', '/api/methodology/drafts', null, { name: 'x' })).status === 401, 'signed out → 401');
  check((await call('POST', '/api/methodology/drafts', out, { name: 'x' })).status === 403, 'personal Gmail (not an exception) → 403');
  check((await call('GET', '/api/methodology/editing', emp)).json?.enabled === true, 'editing status: enabled');

  // Draft → save → conflict → preview → publish (company employee).
  const active0 = (await call('GET', '/api/methodology/active', emp)).json.version.id;
  const d = (await call('POST', '/api/methodology/drafts', emp, { name: 'PG: open land and exits' })).json;
  const id = d.version.id;
  const rules = d.rules;
  const crit = (k) => rules.criteria.find(c => c.key === k);
  crit('exit').maxPoints = 25; crit('exit').fullAt = 0.5; crit('exit').zeroAt = 4;
  crit('openLand').cases[0].points += 5;
  crit('propertyClass').defaultPoints = 1;
  crit('truckingOnSite').explanation = 'Edited in the PostgreSQL end-to-end test.';
  let r = await call('PUT', `/api/methodology/drafts/${id}`, emp, { name: 'PG: open land and exits', changeSummary: 'e2e', rules, expectedRevision: 1 });
  check(r.status === 200 && r.json.version.revision === 2, `employee saves through the writer login (revision → ${r.json?.version?.revision})`);
  r = await call('PUT', `/api/methodology/drafts/${id}`, col, { name: 'stale', changeSummary: null, rules, expectedRevision: 1 });
  check(r.status === 409 && r.json.code === 'draft_conflict' && r.json.title.includes(EMPLOYEE), 'colleague\'s save on an outdated revision → 409 naming the employee');
  r = await call('PUT', `/api/methodology/drafts/${id}`, emp, { name: 'bad', rules: { ...rules, criteria: rules.criteria.map(c => c.key === 'exit' ? { ...c, maxPoints: 1000 } : c) }, expectedRevision: 2 });
  check(r.status === 400, 'invalid points rejected before saving');
  const pv = await call('POST', `/api/methodology/versions/${id}/preview?topN=5`, emp);
  if (pv.status !== 200) console.log(`  preview → ${pv.status} ${pv.text}
--- API log (tail) ---
${apiLog.slice(-4000)}`);
  check(pv.status === 200 && pv.json.scoresChanged > 0, `preview stores ${pv.json?.sites} proposed scores (${pv.json?.scoresChanged} change) via the writer`);
  const busyDouble = await Promise.all([1, 2].map(() => call('POST', `/api/methodology/versions/${id}/publish`, emp,
    { confirm: true, expectedActiveVersionId: active0, rulesHash: pv.json.rulesHash, note: 'pg e2e' })));
  check(busyDouble.filter(x => x.status === 200).length === 1 && busyDouble.filter(x => x.status === 409).length === 1,
    `double-clicked Apply Scoring applies once (${busyDouble.map(x => x.status).join(', ')})`);
  check((await call('GET', '/api/methodology/active', emp)).json.version.id === id, 'the draft is now the live method');

  // Consistency: live columns == the version's stored scores; site data untouched.
  const mism = (await admin.query(`SELECT count(*)::int n FROM c3."CandidateSites" c JOIN c3."SiteVersionScores" v
     ON v."SiteId" = c."SiteId" AND v."MethodologyVersionId" = $1 WHERE c."Score" <> v."Score" OR c."ComputedRank" <> v."Rank" OR c."ScoreVersionId" <> $1`, [id])).rows[0].n;
  check(mism === 0, 'every site\'s live score, rank and version equal the published version (0 mismatches)');
  check(await siteDataHash() === siteDataBefore, 'no non-score column of CandidateSites changed');
  {
    const mid = await fingerprint();
    const changed = userTables.filter(t => mid[t].n !== before[t].n || mid[t].h !== before[t].h);
    check(changed.length === 0, `right after publishing, all populated user tables are byte-identical${changed.length ? ' — CHANGED: ' + changed.join(', ') : ''}`);
  }
  const top = (await call('GET', '/api/sites?pageSize=5&sortBy=Score&descending=true', emp)).json.items[0];
  const dbTop = (await admin.query(`SELECT "Score" FROM c3."CandidateSites" WHERE "Id" = $1`, [top.id])).rows[0].Score;
  const det = (await call('GET', `/api/sites/${top.id}`, emp)).json;
  const csv = (await call('GET', '/api/sites/export', emp)).text;
  check(top.score === dbTop && det.summary.score === dbTop && csv.includes(top.siteId), 'dashboard list, site detail and CSV export show the new scores');

  // Audit with UID + verified e-mail.
  const log = (await call('GET', `/api/methodology/changes?versionId=${id}`, emp)).json.entries;
  check(log.map(e => e.action).join(',') === 'Publish,Preview,SaveDraft,CreateDraft' && log.every(e => e.actorEmail === EMPLOYEE && /^[0-9a-f-]{36}$/.test(e.actorUserId)),
    `audit trail: ${log.map(e => e.action).join(' ← ')} — all by ${EMPLOYEE} with Supabase user id`);
  const auditRows = (await admin.query(`SELECT count(*)::int n FROM c3."MethodologyChangeLog" WHERE "VersionId" = $1`, [id])).rows[0].n;
  check(auditRows === 4, 'audit rows are in PostgreSQL (written by the writer role)');

  // Other changes are still read-only.
  for (const [m, p, b] of [['POST', '/api/saved-sites', { siteId: top.siteId }], ['POST', '/api/contacts', { siteId: top.siteId, name: 'x' }],
    ['PUT', '/api/feasibility', { siteId: top.siteId, itemKey: 'Zoning', status: 'Concern' }], ['POST', '/api/custom-fields', { label: 'x', type: 'Boolean', source: 'Manual' }]]) {
    r = await call(m, p, emp, b);
    check(r.status === 403 && r.json?.code === 'read_only', `${m} ${p} → still 403 read_only`);
  }

  // Revocation: a banned editor is refused at once.
  const banned = await signIn(COLLEAGUE);
  await authPost('/__test/ban', { email: COLLEAGUE });
  check((await call('POST', '/api/methodology/drafts', banned, { name: 'x' })).status === 401, 'banned editor → 401 immediately');

  // Database failure: the writer loses UPDATE on one score column mid-flight → publish fails, nothing changes.
  const d2 = (await call('POST', '/api/methodology/drafts', adm, { name: 'PG: will fail' })).json;
  d2.rules.criteria.find(c => c.key === 'port').defaultPoints = 2;
  await call('PUT', `/api/methodology/drafts/${d2.version.id}`, adm, { name: 'PG: will fail', rules: d2.rules, expectedRevision: 1 });
  const pv2 = (await call('POST', `/api/methodology/versions/${d2.version.id}/preview?topN=5`, adm)).json;
  await admin.query(`REVOKE UPDATE ("TruckDemandPoints") ON c3."CandidateSites" FROM c3_methodology_writer`);
  const liveBefore = (await admin.query(`SELECT md5(string_agg("SiteId"||':'||"Score"||':'||"ComputedRank", ',' ORDER BY "SiteId")) h FROM c3."CandidateSites"`)).rows[0].h;
  r = await call('POST', `/api/methodology/versions/${d2.version.id}/publish`, adm, { confirm: true, expectedActiveVersionId: id, rulesHash: pv2.rulesHash });
  check(r.status === 503 && r.json?.code === 'methodology_failed' && !/permission|42501|denied|Npgsql/i.test(r.text), `database failure → 503, generic message (${r.status})`);
  const liveAfter = (await admin.query(`SELECT md5(string_agg("SiteId"||':'||"Score"||':'||"ComputedRank", ',' ORDER BY "SiteId")) h FROM c3."CandidateSites"`)).rows[0].h;
  check(liveAfter === liveBefore && (await call('GET', '/api/methodology/active', emp)).json.version.id === id, 'failed publish fully rolled back (scores, ranks, active version unchanged)');
  await admin.query(`GRANT UPDATE ("TruckDemandPoints") ON c3."CandidateSites" TO c3_methodology_writer`);

  // Rollback of a methodology: the Gmail administrator (exception) restores the previous version.
  const pv3 = (await call('POST', `/api/methodology/versions/${active0}/preview?topN=5`, adm)).json;
  r = await call('POST', `/api/methodology/versions/${active0}/publish`, adm, { confirm: true, expectedActiveVersionId: id, rulesHash: pv3.rulesHash, note: 'restore' });
  const restoreLog = (await call('GET', `/api/methodology/changes?versionId=${active0}`, emp)).json.entries[0];
  check(r.status === 200 && restoreLog.action === 'Restore' && restoreLog.actorUserId === ADMIN_ID && restoreLog.actorEmail === ADMIN_EMAIL,
    'Gmail administrator exception restores the previous version (audited with UID + e-mail)');
  check((await admin.query(`SELECT count(*)::int n FROM c3."CandidateSites" WHERE "ScoreVersionId" <> $1`, [active0])).rows[0].n === 0, 'every site is back on the restored version');
  await stopApi();

  // ---------- 5. Rollback of the feature ----------
  await admin.query(`ALTER ROLE c3_migrator LOGIN; GRANT USAGE, CREATE ON SCHEMA c3 TO c3_migrator;`);
  console.log('  ' + cloudCopy('methodology-editing-rollback', migratorConn).trim());
  await w.end().catch(() => {});
  await admin.query(sql('09-methodology-writer-role-rollback.sql'));
  await admin.query(sql('04-lockdown.sql'));
  check((await admin.query(`SELECT count(*)::int n FROM pg_roles WHERE rolname = 'c3_methodology_writer'`)).rows[0].n === 0, 'writer login removed (09-methodology-writer-role-rollback.sql)');
  const isoAfter = (await admin.query(sql('05-check-isolation.sql'))).rows;
  check(isoAfter.every(x => x.result === 'OK'), 'after rollback: isolation check all OK (writer gone)');
  const final = await fingerprint();
  const nonMethodology = Object.keys(before).filter(t => !['MethodologyVersions', 'MethodologyActivations', 'MethodologyState', 'SiteVersionScores', 'CandidateSites'].includes(t));
  check(nonMethodology.every(t => final[t].n === before[t].n && final[t].h === before[t].h),
    `after the whole run, all ${nonMethodology.length} non-methodology tables are byte-identical (saved sites, contacts, research, …)`);
  check(await siteDataHash() === siteDataBefore, 'after the whole run, site data (non-score columns) is identical');
  check((await admin.query(`SELECT count(*)::int n FROM c3."MethodologyChangeLog"`)).rows[0].n >= 8, 'rollback kept the audit history');
  await startApi({});   // the API as deployed today: no writer, editing off
  const emp2 = await signIn(EMPLOYEE);
  r = await call('POST', '/api/methodology/drafts', emp2, { name: 'x' });
  check(r.status === 403 && r.json?.code === 'read_only', 'API without the feature flag: methodology changes → 403 read_only');
  check((await call('GET', '/api/methodology/editing', emp2)).json?.enabled === false, 'editing status: disabled');
  check((await call('GET', `/api/methodology/versions/${id}`, emp2)).status === 200 && (await call('GET', '/api/methodology/changes', emp2)).json.total >= 8,
    'versions and audit trail remain readable');
  await stopApi();
} catch (e) {
  failures++;
  console.error('FAIL  unexpected error:', e);
} finally {
  for (const f of cleanup.reverse()) { try { await f(); } catch { /* ignore */ } }
}
console.log(`\n${passes} passed, ${failures} failed`);
process.exit(failures ? 1 : 0);
