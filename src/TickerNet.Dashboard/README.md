# TickerNet Dashboard Authentication

Simple, clean authentication for your TickerNet Dashboard.

## 🚀 Quick Examples

### No Authentication (Public Dashboard)
```csharp
services.AddTickerNet<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        // No authentication setup = public dashboard
    });
});
```

### Basic Authentication
```csharp
services.AddTickerNet<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret123");
    });
});
```

### API Key Authentication
```csharp
services.AddTickerNet<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithApiKey("my-secret-api-key-12345");
    });
});
```

### Use Host Application's Authentication
```csharp
services.AddTickerNet<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication();
    });
});
```

### Use Host Authentication with Custom Policy
```csharp
services.AddTickerNet<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication("AdminPolicy");
    });
});
```

### Dedicated OpenAPI Group
```csharp
services.AddTickerNet<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.SetGroupName("tickernet");
    });
});
```

## 🔧 Fluent API Methods

- `WithBasicAuth(username, password)` - Enable username/password authentication
- `WithApiKey(apiKey)` - Enable API key authentication  
- `WithHostAuthentication(policy)` - Use your app's existing auth with optional policy (e.g., "AdminPolicy")
- `SetBasePath(path)` - Set dashboard URL path
- `SetBackendDomain(domain)` - Set backend API domain
- `SetCorsPolicy(policy)` - Configure CORS
- `SetGroupName(name)` - Set OpenAPI group name for dashboard endpoints
- `DisableTickerQPathRedirect()` - Stop redirecting TickerQ's old dashboard address (see below)

## Coming from TickerQ

TickerQ served the dashboard at `/tickerq/dashboard`. TickerNet redirects GET and HEAD requests for that address,
and anything under it, to the same page under the dashboard's base path (`/tickernet/dashboard` by default). The
rest of the path and the query string are kept, so old links and bookmarks still work. The redirect is temporary
(HTTP 302), so browsers don't cache it. Turn it off with `DisableTickerQPathRedirect()`. It is never added when
the base path is `/tickerq/dashboard` itself.

Saved sign-ins and the chosen dashboard time zone also carry over. When the dashboard opens, it copies the old
browser-storage keys (`tickerq_basic_auth`, `tickerq_api_key`, `tickerq_host_access_key`, `tickerq_bearer_token`
and `tickerq:dashboard:timezone`) to their `tickernet` names, then removes the old keys. It copies a value only
when the new key isn't set yet.

## 🔒 How It Works

The dashboard automatically detects your authentication method:

1. **No auth configured** → Public dashboard
2. **Basic auth configured** → Username/password login
3. **Bearer token configured** → API key authentication
4. **Host auth configured** → Delegates to your app's auth system

## 🌐 Frontend Integration

The frontend automatically adapts based on your backend configuration:
- Shows appropriate login UI
- Handles SignalR authentication 
- Supports both header and query parameter auth (for WebSockets)

That's it! Simple and clean. 🎉
