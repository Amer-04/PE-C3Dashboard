import { Injectable, signal } from '@angular/core';

const KEY = 'c3.recordedBy';

/**
 * The researcher's self-reported name, attached to research records as "Recorded by". The app has no login, so this
 * is free text and is always labelled as self-reported, never as an authenticated identity. Remembered in this
 * browser only, as a convenience.
 */
@Injectable({ providedIn: 'root' })
export class RecordedByStore {
  readonly name = signal(RecordedByStore.read());

  set(value: string) {
    const v = value.trim().slice(0, 200);
    this.name.set(v);
    try { v ? localStorage.setItem(KEY, v) : localStorage.removeItem(KEY); } catch { /* storage unavailable: keep in memory */ }
  }

  /** The value to send, or null when blank. */
  value(): string | null { return this.name().trim() || null; }

  private static read(): string {
    try { return localStorage.getItem(KEY) ?? ''; } catch { return ''; }
  }
}
