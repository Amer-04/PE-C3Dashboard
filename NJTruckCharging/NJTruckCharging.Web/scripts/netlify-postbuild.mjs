// Netlify post-build step: writes the public runtime settings (app-config.json) and the security headers (_headers)
// into the built site. Runs after `ng build` on Netlify; values come from the Netlify site's environment variables:
//
//   C3_API_BASE_URL           https://<render-service>.onrender.com           (required)
//   SUPABASE_URL              https://<project-ref>.supabase.co                (required)
//   SUPABASE_PUBLISHABLE_KEY  the project's publishable / anon key            (required; public by design)
//   C3_SELF_REGISTRATION      "true" shows "Create account" (only after sign-ups, custom SMTP and the Before User
//                             Created hook are set up in Supabase); anything else hides it      (optional, default off)
//
// Only public values may reach the browser. The build FAILS if the key looks like a secret / service-role key or if
// a URL is not https, so a privileged credential can never be published by mistake.
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const out = process.argv[2] ?? 'dist/NJTruckCharging.Web/browser';
const fail = (msg) => { console.error(`netlify-postbuild: ${msg}`); process.exit(1); };

const env = (name) => (process.env[name] ?? '').trim();
const apiBaseUrl = env('C3_API_BASE_URL').replace(/\/+$/, '');
const supabaseUrl = env('SUPABASE_URL').replace(/\/+$/, '');
const key = env('SUPABASE_PUBLISHABLE_KEY');
const selfRegistration = env('C3_SELF_REGISTRATION') === 'true';

for (const [name, value] of [['C3_API_BASE_URL', apiBaseUrl], ['SUPABASE_URL', supabaseUrl]]) {
  if (!value) fail(`${name} is not set.`);
  let u;
  try { u = new URL(value); } catch { fail(`${name} is not a URL.`); }
  if (u.protocol !== 'https:') fail(`${name} must use https.`);
  if (u.pathname !== '/' || u.search || u.hash) fail(`${name} must be the bare origin, e.g. https://example.com`);
}
if (!key) fail('SUPABASE_PUBLISHABLE_KEY is not set.');
if (/^sb_secret_/i.test(key)) fail('SUPABASE_PUBLISHABLE_KEY is a SECRET key. Use the publishable (or anon) key; secret keys stay on the server.');
if (key.split('.').length === 3) {
  let role = '';
  try { role = JSON.parse(Buffer.from(key.split('.')[1], 'base64url').toString('utf8')).role ?? ''; } catch { /* not a JWT */ }
  if (role !== 'anon') fail(`SUPABASE_PUBLISHABLE_KEY is a "${role || 'unknown'}" key. Only the anon/publishable key may be used in the browser.`);
}
if (!existsSync(join(out, 'index.html'))) fail(`no build output at ${out}`);

writeFileSync(join(out, 'app-config.json'),
  JSON.stringify({ apiBaseUrl, supabaseUrl, supabasePublishableKey: key, readOnly: true, selfRegistration }, null, 2) + '\n');

// Strict Content-Security-Policy: inline scripts in index.html are allowed only by their exact hash.
const html = readFileSync(join(out, 'index.html'), 'utf8');
const hashes = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)]
  .map(m => `'sha256-${createHash('sha256').update(m[1], 'utf8').digest('base64')}'`);

const google = 'https://*.googleapis.com https://*.gstatic.com https://*.google.com https://*.ggpht.com https://*.googleusercontent.com';
const csp = [
  `default-src 'self'`,
  `script-src 'self' ${hashes.join(' ')} ${google}`,
  `style-src 'self' 'unsafe-inline' https://fonts.googleapis.com`,
  `font-src 'self' https://fonts.gstatic.com`,
  `img-src 'self' data: blob: https://tile.openstreetmap.org ${google}`,
  `connect-src 'self' ${apiBaseUrl} ${supabaseUrl} https://tile.openstreetmap.org ${google} data: blob:`,
  `worker-src 'self' blob:`,
  `frame-src https://*.google.com`,
  `object-src 'none'`,
  `base-uri 'self'`,
  `form-action 'self'`,
  `frame-ancestors 'none'`,
  `upgrade-insecure-requests`,
].join('; ');

writeFileSync(join(out, '_headers'), `/*
  Content-Security-Policy: ${csp}
  Strict-Transport-Security: max-age=31536000; includeSubDomains
  X-Content-Type-Options: nosniff
  X-Frame-Options: DENY
  Referrer-Policy: strict-origin-when-cross-origin
  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
  Cross-Origin-Opener-Policy: same-origin

/index.html
  Cache-Control: no-cache

/app-config.json
  Cache-Control: no-cache
`);

console.log(`netlify-postbuild: wrote app-config.json and _headers (API ${apiBaseUrl}, Supabase ${supabaseUrl}, self-registration ${selfRegistration ? 'ON' : 'off'}, ${hashes.length} inline script hash(es)).`);
