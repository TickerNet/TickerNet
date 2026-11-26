import { describe, it, expect, vi, beforeEach } from 'vitest';

/**
 * Issue #77: the server sends `UpdateNodeHeartBeat`
 * (TickerNetNotificationHubSender.UpdateNodeHeartBeatAsync), but the dashboard
 * hub client registered no handler for it, so the browser logged
 * "Warning: No client method with the name 'updatenodeheartbeat' found."
 *
 * `startConnection()` registers a no-op handler for every name in `methodName`,
 * so the fix is one entry there. This test fails before the fix: the fake
 * connection records every `on()` call and `UpdateNodeHeartBeat` is missing.
 */

// Fake SignalR connection: records handler registrations, never connects.
const onMock = vi.fn();
const offMock = vi.fn();
const startMock = vi.fn().mockResolvedValue(undefined);
const stopMock = vi.fn().mockResolvedValue(undefined);
const invokeMock = vi.fn().mockResolvedValue(undefined);

vi.mock('@microsoft/signalr', () => {
    class FakeHubConnection {
        state = 'Disconnected';
        on = onMock;
        off = offMock;
        start = startMock;
        stop = stopMock;
        invoke = invokeMock;
    }
    class FakeHubConnectionBuilder {
        withUrl() { return this; }
        withAutomaticReconnect() { return this; }
        configureLogging() { return this; }
        build() { return new FakeHubConnection(); }
    }
    return {
        HubConnectionBuilder: FakeHubConnectionBuilder,
        HttpTransportType: { WebSockets: 1 },
        LogLevel: { Information: 2 },
        HubConnectionState: { Connected: 'Connected', Connecting: 'Connecting', Disconnected: 'Disconnected' },
    };
});

// The hub URL comes from the path resolver; keep it local and static.
vi.mock('@/utilities/pathResolver', () => ({
    getBasePath: () => '/',
    getBackendUrl: () => '',
}));

describe('tickerNotificationHub', () => {
    beforeEach(() => {
        onMock.mockClear();
        startMock.mockClear();
    });

    it('registers a handler for UpdateNodeHeartBeat when the connection starts', async () => {
        // Dynamic import so the mocks above are in place before the module
        // singleton is created.
        const hub = (await import('@/hub/tickerNotificationHub')).default;

        await hub.startConnection();

        expect(startMock).toHaveBeenCalledTimes(1);
        const names = onMock.mock.calls.map((c) => c[0]);
        expect(names).toContain('UpdateNodeHeartBeat');
    });
});
