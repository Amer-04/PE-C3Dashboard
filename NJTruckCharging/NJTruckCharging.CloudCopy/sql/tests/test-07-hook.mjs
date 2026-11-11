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
  const id = (d) => { const n = String(d); return `${n.repeat(8)}-${n.repeat(4)}-${n.repeat(4)}-${n.repeat(4)}-${n.repeat(12)}`; };
  await db.query(`INSERT INTO auth.users (id, email, email_confirmed_at, banned_until) VALUES
    ('${id(1)}', 'listed@poweredison.com', now(), NULL),
    ('${id(2)}', 'listed.personal@gmail.com', now(), NULL),
    ('${id(3)}', 'unlisted@ev-edison.com', now(), NULL),
    ('${id(4)}', 'unlisted@mail.poweredison.com', now(), NULL),
    ('${id(5)}', 'listed.banned@poweredison.com', now(), now() + interval '10 years'),
    ('${id(6)}', 'admin.listed.exception@gmail.com', now(), NULL),
    ('${id(7)}', 'admin.exception.only@gmail.com', now(), NULL),
    ('${id(8)}', 'listed.unconfirmed@ev-edison.com', NULL, NULL)`);
  // Exactly what an administrator does: paste the two Render values, once each, between the quotes.
  const fill = (allowed, exceptions) => inventorySql
    .replace("'' ::text AS allowed_ids", `'${allowed}' ::text AS allowed_ids`)
    .replace("'' ::text AS exception_ids", `'${exceptions}' ::text AS exception_ids`);
  const filled = fill(`${id(1)}, ${id(2)}\n${id(5)};${id(6)} ${id(8)},99999999-9999-9999-9999-999999999999`, `${id(6)}, ${id(7)}`);
  check(filled !== inventorySql, 'inventory: both paste points found');
  const inv = (await db.query(filled)).rows;
  const verdict = Object.fromEntries(inv.map(r => [r.email, r.verdict]));
  const expect = {
    'listed@poweredison.com': 'keeps access (step 1 and 3)',
    'listed.personal@gmail.com': 'LOSES ACCESS at step 1 (personal address, not an exception)',
    'listed.banned@poweredison.com': 'NO ACCESS (unconfirmed, banned or anonymous)',
    'listed.unconfirmed@ev-edison.com': 'NO ACCESS (unconfirmed, banned or anonymous)',
    'unlisted@ev-edison.com': 'GAINS ACCESS at step 3 (company address)',
    'unlisted@mail.poweredison.com': 'no access (before and after)',
    'admin.listed.exception@gmail.com': 'keeps access (step 1 and 3)',
    'admin.exception.only@gmail.com': 'exception NOT on allowlist: no access at step 1, access at step 3',
    '(no account)': 'STALE ID: no such account, remove it from Render',
  };
  for (const [email, v] of Object.entries(expect)) check(verdict[email] === v, `inventory: ${email} -> ${v} (got ${verdict[email]})`);
  check(inv[0].ids_on_allowlist === '6' && inv[0].ids_as_exception === '2', 'inventory: id counts shown (6 allowlisted, 2 exceptions)');
  const unfilled = (await db.query(inventorySql)).rows;
  check(unfilled.every(r => !r.on_allowlist && !r.exception) && unfilled[0].ids_on_allowlist === '0',
    'inventory with nothing pasted treats nobody as approved and shows 0 ids');
} finally {
  await db.end().catch(() => {});
  await server.stop();
}
console.log(failures ? `\n${failures} FAILED` : '\nall passed');
process.exit(failures ? 1 : 0);
