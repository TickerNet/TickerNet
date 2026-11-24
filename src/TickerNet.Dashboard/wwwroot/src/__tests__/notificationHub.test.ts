import { describe, expect, it, vi } from 'vitest'

describe('dashboard notification hub', () => {
  it('registers a handler for the node heartbeat the server sends', async () => {
    // The module exports a singleton that builds a SignalR connection, which
    // needs a window; the test only reads the handler names.
    vi.stubGlobal('window', {
      TickerNetConfig: { basePath: 'http://localhost' },
      location: { protocol: 'http:', host: 'localhost' },
      localStorage: { getItem: () => null },
    })
    const { methodName } = await import('../hub/tickerNotificationHub')
    // Without this the browser logs "No client method with the name
    // 'updatenodeheartbeat' found" on every heartbeat.
    expect(Object.values(methodName)).toContain('UpdateNodeHeartBeat')
  })
})
