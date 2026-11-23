import { describe, it, expect } from 'vitest';
import {
    LEGACY_STORAGE_KEYS,
    migrateLegacyStorage,
    migrateLegacyBrowserStorage,
} from '../utilities/legacyStorage';

class MemoryStorage implements Storage {
    private items = new Map<string, string>();
    get length() { return this.items.size; }
    clear() { this.items.clear(); }
    getItem(key: string) { return this.items.has(key) ? this.items.get(key)! : null; }
    key(index: number) { return Array.from(this.items.keys())[index] ?? null; }
    removeItem(key: string) { this.items.delete(key); }
    setItem(key: string, value: string) { this.items.set(key, String(value)); }
}

describe('migrateLegacyStorage', () => {
    it('copies every old key to its new name and removes the old key', () => {
        const storage = new MemoryStorage();
        storage.setItem('tickerq_basic_auth', 'basic');
        storage.setItem('tickerq_api_key', 'api');
        storage.setItem('tickerq_host_access_key', 'host');
        storage.setItem('tickerq_bearer_token', 'bearer');
        storage.setItem('tickerq:dashboard:timezone', 'Asia/Singapore');

        const copied = migrateLegacyStorage(storage);

        expect(copied).toBe(5);
        expect(storage.getItem('tickernet_basic_auth')).toBe('basic');
        expect(storage.getItem('tickernet_api_key')).toBe('api');
        expect(storage.getItem('tickernet_host_access_key')).toBe('host');
        expect(storage.getItem('tickernet_bearer_token')).toBe('bearer');
        expect(storage.getItem('tickernet:dashboard:timezone')).toBe('Asia/Singapore');
        for (const [oldKey] of LEGACY_STORAGE_KEYS) {
            expect(storage.getItem(oldKey)).toBeNull();
        }
    });

    it('keeps an existing new value and still removes the old key', () => {
        const storage = new MemoryStorage();
        storage.setItem('tickerq_api_key', 'old');
        storage.setItem('tickernet_api_key', 'new');

        const copied = migrateLegacyStorage(storage);

        expect(copied).toBe(0);
        expect(storage.getItem('tickernet_api_key')).toBe('new');
        expect(storage.getItem('tickerq_api_key')).toBeNull();
    });

    it('leaves other keys alone and does nothing without old keys', () => {
        const storage = new MemoryStorage();
        storage.setItem('something_else', 'x');

        expect(migrateLegacyStorage(storage)).toBe(0);
        expect(storage.getItem('something_else')).toBe('x');
        expect(storage.length).toBe(1);
    });

    it('copies an empty string value', () => {
        const storage = new MemoryStorage();
        storage.setItem('tickerq_bearer_token', '');

        expect(migrateLegacyStorage(storage)).toBe(1);
        expect(storage.getItem('tickernet_bearer_token')).toBe('');
    });

    it('handles missing or throwing storage', () => {
        expect(migrateLegacyStorage(undefined)).toBe(0);
        expect(migrateLegacyStorage(null)).toBe(0);

        const throwing = new MemoryStorage();
        throwing.setItem('tickerq_api_key', 'api');
        throwing.setItem = () => { throw new Error('QuotaExceededError'); };
        expect(() => migrateLegacyStorage(throwing)).not.toThrow();
    });
});

describe('migrateLegacyBrowserStorage', () => {
    it('migrates localStorage and sessionStorage separately', () => {
        const local = new MemoryStorage();
        const session = new MemoryStorage();
        local.setItem('tickerq_basic_auth', 'local-basic');
        session.setItem('tickerq_api_key', 'session-api');

        const copied = migrateLegacyBrowserStorage({ localStorage: local, sessionStorage: session } as unknown as Window);

        expect(copied).toBe(2);
        expect(local.getItem('tickernet_basic_auth')).toBe('local-basic');
        expect(session.getItem('tickernet_api_key')).toBe('session-api');
        expect(local.getItem('tickernet_api_key')).toBeNull();
        expect(session.getItem('tickernet_basic_auth')).toBeNull();
    });

    it('skips a storage area that throws on access', () => {
        const local = new MemoryStorage();
        local.setItem('tickerq_api_key', 'api');
        const target = {
            localStorage: local,
            get sessionStorage(): Storage { throw new Error('SecurityError'); },
        } as unknown as Window;

        expect(migrateLegacyBrowserStorage(target)).toBe(1);
        expect(local.getItem('tickernet_api_key')).toBe('api');
    });

    it('does nothing without a window', () => {
        expect(migrateLegacyBrowserStorage(undefined)).toBe(0);
    });
});
