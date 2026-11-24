/**
 * Sign-in expiry for the dashboard.
 *
 * The dashboard keeps credentials in localStorage and sends them with every request, so there is no server-side
 * session to expire. Instead, the time a sign-in runs out is stored next to the credentials, and both are removed
 * once it has passed. A stored sign-in with no expiry (for example one carried over from an older version) counts as
 * expired, so it has to be entered again once.
 */

export const SIGN_IN_EXPIRES_AT_KEY = 'tickernet_sign_in_expires_at';

export const CREDENTIAL_KEYS = [
  'tickernet_basic_auth',
  'tickernet_api_key',
  'tickernet_host_access_key',
] as const;

/** setTimeout's longest delay; longer timers fire immediately. */
const MAX_TIMER_MS = 2_147_483_647;

/** Records that a sign-in made at `now` lasts `timeoutMinutes`. */
export function recordSignIn(storage: Storage, timeoutMinutes: number, now: number = Date.now()): void {
  const minutes = Number.isFinite(timeoutMinutes) && timeoutMinutes > 0 ? timeoutMinutes : 60;
  storage.setItem(SIGN_IN_EXPIRES_AT_KEY, String(now + minutes * 60_000));
}

/** Removes the stored credentials and their expiry. */
export function clearSignIn(storage: Storage): void {
  for (const key of CREDENTIAL_KEYS) {
    storage.removeItem(key);
  }
  storage.removeItem(SIGN_IN_EXPIRES_AT_KEY);
}

function hasCredentials(storage: Storage): boolean {
  return CREDENTIAL_KEYS.some((key) => !!storage.getItem(key));
}

function expiresAt(storage: Storage): number | null {
  const value = Number(storage.getItem(SIGN_IN_EXPIRES_AT_KEY));
  return Number.isFinite(value) && value > 0 ? value : null;
}

/**
 * Clears a stored sign-in whose time has run out, or that has no expiry. Returns true when it cleared one.
 */
export function clearSignInIfExpired(storage: Storage, now: number = Date.now()): boolean {
  if (!hasCredentials(storage)) {
    return false;
  }

  const expiry = expiresAt(storage);
  if (expiry !== null && expiry > now) {
    return false;
  }

  clearSignIn(storage);
  return true;
}

/** Milliseconds until the stored sign-in runs out, capped to what setTimeout accepts; null when there is none. */
export function msUntilSignInExpires(storage: Storage, now: number = Date.now()): number | null {
  const expiry = expiresAt(storage);
  if (expiry === null || !hasCredentials(storage)) {
    return null;
  }
  return Math.min(Math.max(expiry - now, 0), MAX_TIMER_MS);
}
