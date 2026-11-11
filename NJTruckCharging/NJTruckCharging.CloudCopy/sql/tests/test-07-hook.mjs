// Local test of sql/07-before-user-created-hook.sql and sql/08-auth-users-inventory.sql on a throw-away PostgreSQL.
// Never connects to Supabase. Needs the npm packages "pg" and "embedded-postgres" in a separate folder:
//   mkdir <tmp>; cd <tmp>; npm i pg embedded-postgres
//   node <repo>/NJTruckCharging/NJTruckCharging.CloudCopy/sql/tests/test-07-hook.mjs <tmp>
// It starts a private PostgreSQL in <tmp>/data on port 54329, creates stand-ins for Supabase's roles and auth.users,
// applies script 07 exactly as written, then checks every shared e-mail vector, the execute rights and script 08.
import { createRequire } from 'node:module';
import { readFileSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const deps = resolve(process.argv[2] ?? '.');
const require = createRequire(join(deps, 'package.json'));
const { default: EmbeddedPostgres } = await import(pathToFileURL(require.resolve('embedded-postgres')).href);
const pg = require('pg');

const here = dirname(fileURLToPath(import.meta.url));
const repoWeb = resolve(here, '../../../NJTruckCharging.Web/src/app/core/employee-email.vectors.json');
const vectors = JSON.parse(readFileSync(repoWeb, 'utf8'));
const hookSql = readFileSync(resolve(here, '../07-before-user-created-hook.sql'), 'utf8');
const inventorySql = readFileSync(resolve(here, '../08-auth-users-inventory.sql'), 'utf8');

const dataDir = join(deps, 'data');
rmSync(dataDir, { recursive: true, force: true });
const server = new EmbeddedPostgres({ databaseDir: dataDir, user: 'postgres', password: 'local-test-only', port: 54329, persistent: false,
  initdbFlags: ['--encoding=UTF8', '--locale=C'], onLog: () => {}, onError: () => {} });   // UTF-8, like Supabase
let failures = 0;
const check = (ok, what) => { if (!ok) failures++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}`); };

await server.initialise();
await server.start();
const db = new pg.Client({ host: '127.0.0.1', port: 54329, user: 'postgres', password: 'local-test-only', database: 'postgres' });
try {
  await db.connect();
  // Stand-ins for the Supabase roles the script refers to.
  for (const r of ['anon', 'authenticated', 'service_role', 'supabase_auth_admin', 'authenticator'])
    await db.query(`CREATE ROLE ${r} NOLOGIN`);

  await db.query(hookSql);                                      // the script exactly as an administrator would run it
  check(true, 'script 07 runs cleanly');
  let rerun = null;
  try { await db.query(hookSql); } catch (e) { rerun = e.message; }
  check(/already exists - stopping/.test(rerun ?? ''), 'running script 07 twice stops without changes');

  const decide = async (email) =>
    (await db.query('SELECT c3_auth.before_user_created($1::jsonb) AS r', [JSON.stringify({ user: { id: 'x', email } })])).rows[0].r;
  for (const e of vectors.allowed) {
    const r = await decide(e);
    check(JSON.stringify(r) === '{}', `allowed  ${JSON.stringify(e)}`);
  }
  for (const e of vectors.refused) {
    const r = await decide(e);
    check(r?.error?.http_code === 403 && typeof r.error.message === 'string', `refused  ${JSON.stringify(e)}`);
  }
  for (const [label, event] of [['no e-mail (phone sign-up)', { user: { phone: '+15550100' } }], ['anonymous user', { user: { is_anonymous: true } }],
                                ['null e-mail', { user: { email: null } }], ['no user object', {}]]) {
    const r = (await db.query('SELECT c3_auth.before_user_created($1::jsonb) AS r', [JSON.stringify(event)])).rows[0].r;
    check(r?.error?.http_code === 403, `refused  ${label}`);
  }

  const rights = (await db.query(`SELECT rolname, has_function_privilege(oid, 'c3_auth.before_user_created(jsonb)', 'EXECUTE') AS x
                                  FROM pg_roles WHERE rolname IN ('anon','authenticated','service_role','supabase_auth_admin','authenticator')`)).rows;
  for (const { rolname, x } of rights)
    check(x === (rolname === 'supabase_auth_admin'), `${rolname} ${x ? 'can' : 'cannot'} execute the hook`);
  await db.query('CREATE ROLE some_new_role NOLOGIN');
  check(!(await db.query(`SELECT has_function_privilege('some_new_role', 'c3_auth.before_user_created(jsonb)', 'EXECUTE') AS x`)).rows[0].x,
    'PUBLIC (any other role) cannot execute the hook');
  await db.query('SET ROLE anon');
  let anonErr = null;
  try { await db.query(`SELECT c3_auth.before_user_created('{"user":{"email":"a@poweredison.com"}}')`); } catch (e) { anonErr = e.message; }
  await db.query('RESET ROLE');
  check(/permission denied/.test(anonErr ?? ''), 'calling the hook as anon is denied');

  // Script 08 against a stand-in auth.users.
  await db.query(`CREATE SCHEMA auth; CREATE TABLE auth.users (id uuid PRIMARY KEY, email text, created_at timestamptz DEFAULT now(),
    last_sign_in_at timestamptz, email_confirmed_at timestamptz, banned_until timestamptz, is_anonymous boolean DEFAULT false,
    raw_app_meta_data jsonb DEFAULT '{"provider":"email"}')`);
  const ids = { a: '11111111-1111-1111-1111-111111111111', b: '22222222-2222-2222-2222-222222222222',
                c: '33333333-3333-3333-3333-333333333333', d: '44444444-4444-4444-4444-444444444444', e: '55555555-5555-5555-5555-555555555555' };
  await db.query(`INSERT INTO auth.users (id, email, email_confirmed_at, banned_until) VALUES
    ('${ids.a}', 'listed@poweredison.com', now(), NULL),
    ('${ids.b}', 'listed.personal@gmail.com', now(), NULL),
    ('${ids.c}', 'unlisted@ev-edison.com', now(), NULL),
    ('${ids.d}', 'unlisted@mail.poweredison.com', now(), NULL),
    ('${ids.e}', 'listed.banned@poweredison.com', now(), now() + interval '10 years')`);
  const filled = inventorySql.replaceAll("'PASTE_AUTH__ALLOWEDUSERIDS_HERE', 'PASTE_AUTH__ALLOWEDUSERIDS_HERE'",
    `'${ids.a}, ${ids.b},${ids.e},99999999-9999-9999-9999-999999999999', 'PASTE_AUTH__ALLOWEDUSERIDS_HERE'`);
  const res = await db.query(filled);
  const [inv, stale] = res;
  const verdict = Object.fromEntries(inv.rows.map(r => [r.email, r.after_switch]));
  check(verdict['listed@poweredison.com'] === 'keeps access', 'inventory: listed company account keeps access');
  check(verdict['listed.personal@gmail.com'] === 'LOSES ACCESS', 'inventory: listed personal account LOSES ACCESS');
  check(verdict['listed.banned@poweredison.com'] === 'LOSES ACCESS', 'inventory: listed banned account LOSES ACCESS');
  check(verdict['unlisted@ev-edison.com'] === 'GAINS ACCESS', 'inventory: unlisted confirmed company account GAINS ACCESS');
  check(verdict['unlisted@mail.poweredison.com'] === 'still refused', 'inventory: subdomain account still refused');
  check(stale.rows.length === 1 && stale.rows[0].allowlisted_id_without_account === '99999999-9999-9999-9999-999999999999',
    'inventory: stale allowlist id reported');
  const unfilled = await db.query(inventorySql);
  check(unfilled[0].rows.every(r => !r.approved_today), 'inventory with the placeholder left in treats nobody as approved');
} finally {
  await db.end().catch(() => {});
  await server.stop();
}
console.log(failures ? `\n${failures} FAILED` : '\nall passed');
process.exit(failures ? 1 : 0);
