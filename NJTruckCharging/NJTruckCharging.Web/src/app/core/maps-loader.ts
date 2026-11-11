import { InjectionToken } from '@angular/core';
import { importLibrary, setOptions } from '@googlemaps/js-api-loader';

let configured = false;
let authFailed = false;
const authFailureListeners = new Set<() => void>();

/**
 * Google calls `window.gm_authFailure` when it rejects the key AFTER the script has loaded (the site's address is not
 * in the key's website restrictions, the Maps JavaScript API is not enabled, billing is off, or the key was deleted).
 * The map object still exists then, but Google shows only a grey error box, so callers switch to their fallback.
 */
export function mapsAuthFailed(): void {
  authFailed = true;
  for (const listener of [...authFailureListeners]) listener();
}

/** Runs `listener` once Google rejects the key (immediately if it already has). Returns an unsubscribe function. */
export function onMapsAuthFailure(listener: () => void): () => void {
  if (authFailed) { listener(); return () => {}; }
  authFailureListeners.add(listener);
  return () => authFailureListeners.delete(listener);
}

/** Loads the Google Maps JavaScript API once (key comes from the API's /api/config/maps). */
export async function loadGoogleMaps(apiKey: string): Promise<google.maps.MapsLibrary> {
  if (!configured) {
    (window as unknown as { gm_authFailure: () => void }).gm_authFailure = mapsAuthFailed;
    setOptions({ key: apiKey, v: 'weekly' });
    configured = true;
  }
  return importLibrary('maps');
}

/** Advanced markers (needs a map ID). Call after {@link loadGoogleMaps}. */
export function loadMarkerLibrary(): Promise<google.maps.MarkerLibrary> {
  return importLibrary('marker');
}

/** The loader as an injectable, so components can be tested without contacting Google. */
export interface MapsLoader {
  load(apiKey: string): Promise<google.maps.MapsLibrary>;
  marker(): Promise<google.maps.MarkerLibrary>;
  onAuthFailure(listener: () => void): () => void;
}
export const MAPS_LOADER = new InjectionToken<MapsLoader>('MAPS_LOADER', {
  providedIn: 'root',
  factory: () => ({ load: loadGoogleMaps, marker: loadMarkerLibrary, onAuthFailure: onMapsAuthFailure }),
});
