import { describe, it, expect } from 'vitest';
import {
  SIGN_IN_EXPIRES_AT_KEY,
  clearSignInIfExpired,
  msUntilSignInExpires,
  recordSignIn,
} from '../utilities/signInExpiry';

class MemoryStorage implements Storage {
  private items = new Map<string, string>();
  get length() { return this.items.size; }
  clear() { this.items.clear(); }
  getItem(key: string) { return this.items.has(key) ? this.items.get(key)! : null; }
  key(index: number) { return Array.from(this.items.keys())[index] ?? null; }
  removeItem(key: string) { this.items.delete(key); }
  setItem(key: string, value: string) { this.items.set(key, String(value)); }
}

const now = 1_700_000_000_000;

describe('sign-in expiry', () => {
  it('keeps a sign-in until its timeout runs out', () => {
    const storage = new MemoryStorage();
    storage.setItem('tickernet_api_key', 'key');
    recordSignIn(storage, 30, now);

    expect(clearSignInIfExpired(storage, now + 29 * 60_000)).toBe(false);
    expect(storage.getItem('tickernet_api_key')).toBe('key');
    expect(msUntilSignInExpires(storage, now + 29 * 60_000)).toBe(60_000);
  });

  it('clears the credentials and the expiry once the timeout has passed', () => {
    const storage = new MemoryStorage();
    storage.setItem('tickernet_basic_auth', 'basic');
    recordSignIn(storage, 30, now);

    expect(clearSignInIfExpired(storage, now + 30 * 60_000)).toBe(true);
    expect(storage.getItem('tickernet_basic_auth')).toBeNull();
    expect(storage.getItem(SIGN_IN_EXPIRES_AT_KEY)).toBeNull();
  });

  it('treats stored credentials with no expiry as expired', () => {
    const storage = new MemoryStorage();
    storage.setItem('tickernet_host_access_key', 'host');

    expect(clearSignInIfExpired(storage, now)).toBe(true);
    expect(storage.getItem('tickernet_host_access_key')).toBeNull();
  });

  it('does nothing when nobody is signed in', () => {
    const storage = new MemoryStorage();

    expect(clearSignInIfExpired(storage, now)).toBe(false);
    expect(msUntilSignInExpires(storage, now)).toBeNull();
  });

  it('falls back to 60 minutes for a missing or invalid timeout', () => {
    const storage = new MemoryStorage();
    storage.setItem('tickernet_api_key', 'key');
    recordSignIn(storage, 0, now);

    expect(storage.getItem(SIGN_IN_EXPIRES_AT_KEY)).toBe(String(now + 60 * 60_000));
  });

  it('caps the timer delay to what setTimeout accepts', () => {
    const storage = new MemoryStorage();
    storage.setItem('tickernet_api_key', 'key');
    recordSignIn(storage, 60 * 24 * 365, now);

    expect(msUntilSignInExpires(storage, now)).toBe(2_147_483_647);
  });
});
