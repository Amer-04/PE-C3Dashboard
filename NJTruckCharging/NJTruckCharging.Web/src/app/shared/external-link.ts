/** A website value from the source data that can be opened safely, plus a short label for display. */
export interface WebsiteLink { href: string; label: string; }

/**
 * Turns a website value from the source data (Overture Maps business listing) into a safe external link.
 * - http(s) URLs are used as recorded;
 * - bare domains ("kampack.com", "www.lakwarehouse.com/...") get an https:// prefix (nothing else is added);
 * - anything else (other schemes such as javascript:, spaces, or values shortened in the source with "/.../")
 *   returns null: it is not a usable link, so none is shown.
 * Shared by the ranked table and Site Details so both open the same address.
 */
export function websiteLink(raw: string | null | undefined): WebsiteLink | null {
  const v = raw?.trim();
  if (!v || /\s/.test(v) || v.includes('...')) return null;
  const candidate = /^https?:\/\//i.test(v) ? v
    : /^[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}([/?#]\S*)?$/i.test(v) ? `https://${v}` : null;
  if (!candidate) return null;
  try {
    const url = new URL(candidate);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return null;
    return { href: url.href, label: url.hostname.replace(/^www\./i, '').toLowerCase() };
  } catch {
    return null;
  }
}
