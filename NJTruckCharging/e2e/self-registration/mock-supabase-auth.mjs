// A LOCAL stand-in for Supabase Auth (GoTrue), for end-to-end tests only. Never used in a deployment.
// Implements just what the C3 dashboard and API use: sign-up (calling the Before User Created hook), the confirmation
// link, password sign-in, refresh, /user, logout, resend and the JWKS. Tokens are ES256 like the real project.
// "E-mails" go to an in-memory outbox that the test reads. Test controls live under /__test/.
import { createServer } from 'node:http';
import { generateKeyPairSync, randomBytes, randomUUID, scryptSync, sign, timingSafeEqual, verify } from 'node:crypto';

const b64u = (b) => Buffer.from(b).toString('base64url');

export function startMockSupabaseAuth({ port, hook, siteUrl, tokenSeconds = 3600 }) {
  const issuer = `http://127.0.0.1:${port}/auth/v1`;
  const { privateKey, publicKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
  const kid = 'e2e-' + randomBytes(4).toString('hex');
  const jwk = { ...publicKey.export({ format: 'jwk' }), kid, alg: 'ES256', use: 'sig', key_ops: ['verify'] };

  const users = new Map();          // id -> user
  const sessions = new Map();       // session_id -> { userId, refresh }
  const outbox = [];                // { to, kind, link }
  const verifyTokens = new Map();   // token -> { userId, type, redirect }
  let signups = 0;

  const hash = (pw, salt = randomBytes(16)) => ({ salt, h: scryptSync(pw, salt, 32) });
  const userJson = (u) => ({
    id: u.id, aud: 'authenticated', role: 'authenticated', email: u.email, email_confirmed_at: u.confirmedAt,
    confirmed_at: u.confirmedAt, banned_until: u.bannedUntil, is_anonymous: false, created_at: u.createdAt,
    app_metadata: { provider: 'email', providers: ['email'] }, user_metadata: {}, identities: u.confirmedAt ? [{ provider: 'email' }] : [],
  });

  function jwt(u, sessionId) {
    const now = Math.floor(Date.now() / 1000);
    const header = b64u(JSON.stringify({ alg: 'ES256', typ: 'JWT', kid }));
    const payload = b64u(JSON.stringify({
      iss: issuer, aud: 'authenticated', sub: u.id, email: u.email, role: 'authenticated', session_id: sessionId,
      is_anonymous: false, aal: 'aal1', iat: now, exp: now + tokenSeconds,
      app_metadata: { provider: 'email' }, user_metadata: { email_verified: true },
    }));
    const sig = sign('sha256', Buffer.from(`${header}.${payload}`), { key: privateKey, dsaEncoding: 'ieee-p1363' });
    return `${header}.${payload}.${b64u(sig)}`;
  }

  /** Verifies one of OUR tokens (signature + expiry) and that its session still exists, like GoTrue's /user. */
  function sessionFor(req) {
    const m = /^Bearer (.+)$/.exec(req.headers.authorization ?? '');
    if (!m) return null;
    const [h, p, s] = m[1].split('.');
    if (!s) return null;
    if (!verify('sha256', Buffer.from(`${h}.${p}`), { key: publicKey, dsaEncoding: 'ieee-p1363' }, Buffer.from(s, 'base64url'))) return null;
    const claims = JSON.parse(Buffer.from(p, 'base64url').toString());
    if (claims.exp < Date.now() / 1000) return null;
    const sess = sessions.get(claims.session_id);
    const u = sess && users.get(sess.userId);
    return u ? { u, sid: claims.session_id } : null;
  }

  function issueSession(u) {
    const sid = randomUUID();
    const refresh = randomBytes(24).toString('hex');
    sessions.set(sid, { userId: u.id, refresh });
    u.lastSignIn = new Date().toISOString();
    return {
      access_token: jwt(u, sid), token_type: 'bearer', expires_in: tokenSeconds,
      expires_at: Math.floor(Date.now() / 1000) + tokenSeconds, refresh_token: refresh, user: userJson(u),
    };
  }

  function mail(u, kind, redirect) {
    const token = randomBytes(16).toString('hex');
    verifyTokens.set(token, { userId: u.id, type: kind, redirect: redirect || siteUrl });
    const link = `${issuer}/verify?token=${token}&type=${kind}&redirect_to=${encodeURIComponent(redirect || siteUrl)}`;
    outbox.push({ to: u.email, kind, link, at: Date.now() });
  }

  const findByEmail = (email) => [...users.values()].find(u => u.email.toLowerCase() === String(email).toLowerCase());

  const server = createServer(async (req, res) => {
    const url = new URL(req.url, issuer);
    const send = (status, body, headers = {}) => {
      res.writeHead(status, {
        'Content-Type': 'application/json',
        'Access-Control-Allow-Origin': req.headers.origin ?? '*',
        'Access-Control-Allow-Headers': req.headers['access-control-request-headers'] ?? '*',
        'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS',
        'Access-Control-Expose-Headers': 'X-Supabase-Api-Version',
        'X-Supabase-Api-Version': '2024-01-01',
        ...headers,
      });
      res.end(body === undefined ? '' : JSON.stringify(body));
    };
    const fail = (status, code, msg) => send(status, { code, error_code: code, msg });
    if (req.method === 'OPTIONS') return send(204);
    let body = {};
    if (req.method === 'POST' || req.method === 'PUT') {
      const chunks = [];
      for await (const c of req) chunks.push(c);
      try { body = chunks.length ? JSON.parse(Buffer.concat(chunks).toString()) : {}; } catch { return fail(400, 'bad_json', 'Bad JSON'); }
    }
    const path = url.pathname.replace(/^\/auth\/v1/, '');
    const needKey = !path.startsWith('/verify') && !path.startsWith('/.well-known') && !url.pathname.startsWith('/__test');
    if (needKey && req.headers.apikey !== 'sb_publishable_e2e') return fail(401, 'no_api_key', 'No API key found in request');

    try {
      if (req.method === 'GET' && path === '/.well-known/jwks.json') return send(200, { keys: [jwk] });

      if (req.method === 'POST' && path === '/signup') {
        signups++;
        const email = String(body.email ?? '');
        const existing = findByEmail(email);
        if (existing) return send(200, { ...userJson(existing), identities: [] });   // GoTrue's "obfuscated" answer
        if (String(body.password ?? '').length < 12) return fail(422, 'weak_password', 'Password should be at least 12 characters.');
        const decision = await hook({ metadata: { name: 'before-user-created' }, user: { id: randomUUID(), email, aud: 'authenticated' } });
        if (decision?.error) return fail(decision.error.http_code ?? 403, 'hook_refused', decision.error.message);
        const u = { id: randomUUID(), email, pw: hash(body.password), confirmedAt: null, bannedUntil: null, createdAt: new Date().toISOString() };
        users.set(u.id, u);
        mail(u, 'signup', body.options?.email_redirect_to ?? url.searchParams.get('redirect_to'));
        return send(200, userJson(u));
      }

      if (req.method === 'POST' && path === '/resend') {
        const u = findByEmail(body.email);
        if (u && !u.confirmedAt) mail(u, 'signup', url.searchParams.get('redirect_to'));
        return send(200, {});
      }

      if (req.method === 'GET' && path === '/verify') {
        const t = verifyTokens.get(url.searchParams.get('token'));
        const to = new URL(url.searchParams.get('redirect_to') || siteUrl);
        if (!t) {
          to.hash = 'error=access_denied&error_code=otp_expired&error_description=Email+link+is+invalid+or+has+expired';
          return send(303, undefined, { Location: to.toString() });
        }
        verifyTokens.delete(url.searchParams.get('token'));
        users.get(t.userId).confirmedAt = new Date().toISOString();
        to.searchParams.set('code', randomBytes(12).toString('hex'));
        return send(303, undefined, { Location: to.toString() });
      }

      if (req.method === 'POST' && path === '/token') {
        const grant = url.searchParams.get('grant_type');
        if (grant === 'password') {
          const u = findByEmail(body.email);
          const ok = u && timingSafeEqual(scryptSync(String(body.password ?? ''), u.pw.salt, 32), u.pw.h);
          if (!ok) return fail(400, 'invalid_credentials', 'Invalid login credentials');
          if (!u.confirmedAt) return fail(400, 'email_not_confirmed', 'Email not confirmed');
          if (u.bannedUntil && new Date(u.bannedUntil) > new Date()) return fail(400, 'user_banned', 'User is banned');
          return send(200, issueSession(u));
        }
        if (grant === 'refresh_token') {
          const entry = [...sessions.entries()].find(([, s]) => s.refresh === body.refresh_token);
          const u = entry && users.get(entry[1].userId);
          if (!u || (u.bannedUntil && new Date(u.bannedUntil) > new Date())) return fail(400, 'refresh_token_not_found', 'Invalid Refresh Token');
          sessions.delete(entry[0]);
          return send(200, issueSession(u));
        }
        return fail(400, 'unsupported_grant_type', 'unsupported grant');
      }

      if (req.method === 'GET' && path === '/user') {
        const s = sessionFor(req);
        return s ? send(200, userJson(s.u)) : fail(403, 'session_not_found', 'Session from session_id claim in JWT does not exist');
      }

      if (req.method === 'POST' && path === '/logout') {
        const s = sessionFor(req);
        if (s) sessions.delete(s.sid);
        return send(204);
      }

      // ---- test controls (an administrator's actions in the Supabase dashboard) ----
      if (url.pathname === '/__test/outbox') return send(200, outbox.filter(m => !url.searchParams.get('to') || m.to === url.searchParams.get('to')));
      if (url.pathname === '/__test/stats') return send(200, { users: users.size, sessions: sessions.size, signups });
      if (url.pathname === '/__test/ban' && req.method === 'POST') {
        const u = findByEmail(body.email); u.bannedUntil = new Date(Date.now() + 876000 * 3600e3).toISOString();
        for (const [sid, s] of sessions) if (s.userId === u.id) sessions.delete(sid);
        return send(200, {});
      }
      if (url.pathname === '/__test/unconfirm' && req.method === 'POST') {
        findByEmail(body.email).confirmedAt = null;
        return send(200, {});
      }
      if (url.pathname === '/__test/delete' && req.method === 'POST') {
        const u = findByEmail(body.email); users.delete(u.id);
        for (const [sid, s] of sessions) if (s.userId === u.id) sessions.delete(sid);
        return send(200, {});
      }
      if (url.pathname === '/__test/create-confirmed' && req.method === 'POST') {
        // An administrator-created account (bypasses the sign-up form, like "Add user" in the dashboard).
        const decision = await hook({ user: { email: body.email } });
        if (decision?.error && !body.bypassHook) return fail(403, 'hook_refused', decision.error.message);
        const u = { id: randomUUID(), email: body.email, pw: hash(body.password), confirmedAt: new Date().toISOString(), bannedUntil: null, createdAt: new Date().toISOString() };
        users.set(u.id, u);
        return send(200, { id: u.id });
      }
      if (url.pathname === '/__test/sign' && req.method === 'POST') {
        // Token for a given user WITHOUT a session (used to prove the API asks Supabase, not just the signature).
        const u = findByEmail(body.email);
        return send(200, { token: jwt(u, randomUUID()) });
      }
      return fail(404, 'not_found', 'Not found');
    } catch (e) {
      console.error('[mock-auth]', e);
      return fail(500, 'unexpected_failure', 'Unexpected failure');
    }
  });
  return new Promise((resolve) => server.listen(port, '127.0.0.1', () => resolve({ server, issuer, close: () => new Promise(r => server.close(r)) })));
}

