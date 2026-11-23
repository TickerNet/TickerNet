// Browser storage keys the dashboard used under its TickerQ name, and the keys that replaced them.
// Copying them over on startup keeps people signed in and keeps their chosen time zone after
// moving from TickerQ to TickerNet.
export const LEGACY_STORAGE_KEYS: ReadonlyArray<readonly [oldKey: string, newKey: string]> = [
  ['tickerq_basic_auth', 'tickernet_basic_auth'],
  ['tickerq_api_key', 'tickernet_api_key'],
  ['tickerq_host_access_key', 'tickernet_host_access_key'],
  ['tickerq_bearer_token', 'tickernet_bearer_token'],
  ['tickerq:dashboard:timezone', 'tickernet:dashboard:timezone'],
]

/**
 * Moves the old keys in one storage area to their new names. A value is copied only when the new
 * key isn't set yet, so a newer value always wins. The old key is removed either way.
 * Returns how many values were copied.
 */
export function migrateLegacyStorage(storage: Storage | null | undefined): number {
  if (!storage) return 0

  let copied = 0
  for (const [oldKey, newKey] of LEGACY_STORAGE_KEYS) {
    try {
      const value = storage.getItem(oldKey)
      if (value === null) continue

      if (storage.getItem(newKey) === null) {
        storage.setItem(newKey, value)
        copied++
      }
      storage.removeItem(oldKey)
    } catch {
      // Storage can be blocked or full; the dashboard still works, people just sign in again.
    }
  }
  return copied
}

/** Runs {@link migrateLegacyStorage} on both localStorage and sessionStorage. */
export function migrateLegacyBrowserStorage(target: Window | undefined = globalThis.window): number {
  if (!target) return 0

  let copied = 0
  for (const area of ['localStorage', 'sessionStorage'] as const) {
    let storage: Storage | undefined
    try {
      storage = target[area]
    } catch {
      // Accessing storage throws when the browser blocks it (for example, some private modes).
      storage = undefined
    }
    copied += migrateLegacyStorage(storage)
  }
  return copied
}
