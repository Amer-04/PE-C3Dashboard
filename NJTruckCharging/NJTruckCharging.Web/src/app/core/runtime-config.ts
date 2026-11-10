import { InjectionToken } from '@angular/core';

/**
 * Deployment settings read at startup from /app-config.json (written by the Netlify build from its environment).
 * Only public values belong here: the API address, the Supabase project URL and its publishable (anon) key. No
 * database credentials, service-role keys or secrets — those exist only on the API server.
 * Without the file (local development) the dashboard runs as before: no sign-in, API on the same origin.
 */
export interface RuntimeConfig {
  /** e.g. https://c3-site-finder-api.onrender.com ('' = same origin, as in local development). */
  apiBaseUrl: string;
  supabaseUrl: string;
  supabasePublishableKey: string;
  readOnly: boolean;
}

export const RUNTIME_CONFIG = new InjectionToken<RuntimeConfig>('RUNTIME_CONFIG');

export const LOCAL_CONFIG: RuntimeConfig = { apiBaseUrl: '', supabaseUrl: '', supabasePublishableKey: '', readOnly: false };

export function authEnabled(c: RuntimeConfig): boolean {
  return !!c.supabaseUrl && !!c.supabasePublishableKey;
}

export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  try {
    const r = await fetch('/app-config.json', { cache: 'no-store' });
    if (!r.ok) return LOCAL_CONFIG;
    const j = await r.json() as Partial<RuntimeConfig>;
    return {
      apiBaseUrl: (j.apiBaseUrl ?? '').replace(/\/+$/, ''),
      supabaseUrl: (j.supabaseUrl ?? '').replace(/\/+$/, ''),
      supabasePublishableKey: j.supabasePublishableKey ?? '',
      readOnly: !!j.readOnly,
    };
  } catch {
    return LOCAL_CONFIG;
  }
}
