import { AuthService } from './auth.service';

/**
 * CSV exports are plain links (<a href="/api/…/export" download>). A link cannot carry the sign-in token, so when
 * sign-in is on, a click on such a link is turned into an authenticated request and the file is saved from it.
 * The API never serves an export without a valid token.
 */
export function installAuthenticatedDownloads(auth: AuthService): () => void {
  if (!auth.enabled) return () => {};
  const onClick = async (ev: MouseEvent) => {
    const link = (ev.target as Element | null)?.closest?.('a[download]') as HTMLAnchorElement | null;
    const href = link?.getAttribute('href');
    if (!link || !href?.startsWith('/api/')) return;
    ev.preventDefault();
    if (link.getAttribute('aria-busy') === 'true') return;
    link.setAttribute('aria-busy', 'true');
    try {
      const token = await auth.accessToken();
      const r = await fetch(auth.apiUrl(href), { headers: token ? { Authorization: `Bearer ${token}` } : {} });
      if (r.status === 401) { auth.sessionRejected(); return; }
      if (r.status === 403) { auth.notApproved(); return; }
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      const url = URL.createObjectURL(await r.blob());
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName(r.headers.get('Content-Disposition'));
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 10_000);
    } catch {
      alert('The download did not work. Please try again.');
    } finally {
      link.removeAttribute('aria-busy');
    }
  };
  document.addEventListener('click', onClick, true);
  return () => document.removeEventListener('click', onClick, true);
}

/** File name from a Content-Disposition header (RFC 5987 filename* first), or a safe default. */
export function fileName(disposition: string | null): string {
  const d = disposition ?? '';
  const star = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(d)?.[1];
  if (star) return decodeURIComponent(star.trim());
  const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(d)?.[1];
  return plain?.trim() || 'export.csv';
}
