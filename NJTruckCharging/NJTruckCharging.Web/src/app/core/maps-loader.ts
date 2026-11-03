import { importLibrary, setOptions } from '@googlemaps/js-api-loader';

let configured = false;

/** Loads the Google Maps JavaScript API once (key comes from the API's /api/config/maps). */
export async function loadGoogleMaps(apiKey: string): Promise<google.maps.MapsLibrary> {
  if (!configured) {
    setOptions({ key: apiKey, v: 'weekly' });
    configured = true;
  }
  return importLibrary('maps');
}

/** Advanced markers (needs a map ID). Call after {@link loadGoogleMaps}. */
export function loadMarkerLibrary(): Promise<google.maps.MarkerLibrary> {
  return importLibrary('marker');
}
