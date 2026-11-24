# TickerNet.Dashboard

Dashboard UI for monitoring and managing TickerNet jobs in an ASP.NET Core app. The package embeds the web app, its HTTP API and the SignalR hub.

## Install

```bash
dotnet add package TickerNet
dotnet add package TickerNet.Dashboard
```

`TickerNet.Dashboard` and the suggested storage packages reference only `TickerNet.Utilities`, so install the core `TickerNet` package explicitly: it defines `AddTickerNet()` and `UseTickerNet()`. Add one operational store as well, for example `TickerNet.MongoDB` or `TickerNet.EntityFrameworkCore`. See the [getting started guide](https://tickernet.github.io/TickerNet/getting-started.html) for the full setup.

## Basic setup

```csharp
using TickerNet.DependencyInjection;
using TickerNet.Dashboard.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerNet(options =>
{
    options.AddDashboard(dashboard =>
    {
        dashboard.SetBasePath("/jobs");
        dashboard.WithBasicAuth("admin", builder.Configuration["Dashboard:Password"]!);
    });
});

var app = builder.Build();
app.UseTickerNet();   // serves the dashboard at /jobs
app.Run();
```

`AddTickerNet` and `UseTickerNet` live in `TickerNet.DependencyInjection`; `AddDashboard` lives in `TickerNet.Dashboard.DependencyInjection`. Pick a sign-in method (as above) or call `WithNoAuth()` explicitly — with no sign-in configured, TickerNet logs a startup warning.

The dashboard is served at `/tickernet/dashboard` by default. Under that path are the web app, the HTTP API at `api/...` and the SignalR hub at `ticker-notification-hub`. Change the path with `SetBasePath("/jobs")`; `UseTickerNet()` then serves it at `/jobs`.

With `WebApplication`, `UseTickerNet()` adds the dashboard. With a `Startup` class, a startup filter registered by `AddDashboard()` adds it instead. When building from source, run `npm ci && npm run build` in `src/TickerNet.Dashboard/wwwroot` before building the project: the dashboard embeds the files from `wwwroot/dist`.

## Sign-in

> **Lock it down before you deploy.** Out of the box the dashboard has no sign-in: anyone who can reach the URL can see, add, run and delete jobs. TickerNet logs a warning at startup until you pick a sign-in method or call `WithNoAuth()`.

The method you call selects the sign-in mode:

| Method | What it does |
| --- | --- |
| `WithNoAuth()` | No sign-in, on purpose. With no sign-in method at all the dashboard is also open, but TickerNet logs a startup warning; this call confirms it and silences the warning. |
| `WithBasicAuth(username, password)` | Username and password, sent as HTTP Basic. |
| `WithApiKey(apiKey)` | A key the user enters, sent as a Bearer token. |
| `WithHostAuthentication(policy = null)` | Your app's own authentication, with an optional authorization policy name. |
| `WithCustomAuth(Func<string, bool> validator)` | Your validator decides for API clients that send their own `Authorization` header. The built-in web app cannot sign in with it (see below). |
| `WithSessionTimeout(minutes)` | How long a sign-in lasts in the browser. When it runs out, the dashboard forgets the stored credentials and asks again, even in an open tab. Default 60 minutes; minimum 1. |

### No sign-in (public dashboard)

```csharp
builder.Services.AddTickerNet(options =>
{
    options.AddDashboard(dashboard => dashboard.WithNoAuth());
});
```

### Basic sign-in

```csharp
builder.Services.AddTickerNet(options =>
{
    options.AddDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", builder.Configuration["Dashboard:Password"]!);
    });
});
```

### API key

```csharp
builder.Services.AddTickerNet(options =>
{
    options.AddDashboard(dashboard =>
    {
        dashboard.WithApiKey(builder.Configuration["Dashboard:ApiKey"]!);
    });
});
```

### The host app's authentication

```csharp
builder.Services.AddTickerNet(options =>
{
    options.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication("AdminPolicy");
    });
});
```

Pass no argument to use the host's default authorization policy.

### Custom validation for API clients

```csharp
builder.Services.AddTickerNet(options =>
{
    options.AddDashboard(dashboard =>
    {
        // The validator receives the Authorization header on /api requests and
        // the access_token query value on the SignalR hub.
        dashboard.WithCustomAuth(value => value == "Bearer:my-secret" || value == "my-secret");
    });
});
```

`WithCustomAuth` is for API clients that send their own `Authorization` header. The embedded web app has no custom sign-in form and sends no `Authorization` header in custom mode, so it cannot sign in — use `WithApiKey` for the built-in key-entry flow. The SignalR hub sends `access_token=Bearer:<token>` or the raw token, never `Bearer <token>`.

## CORS

Without `SetCorsPolicy(...)`, the dashboard accepts only same-origin requests, which is all its own web app needs. If you use `SetBackendDomain(...)` and no policy, it allows any origin with credentials so a separately hosted front end keeps working, and logs a warning; set a policy that names the origins that serve the dashboard.

```csharp
options.AddDashboard(dashboard =>
{
    dashboard.SetCorsPolicy(policy => policy
        .WithOrigins("https://dashboard.example.com")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});
```

## All dashboard options

Every public member of `DashboardOptionsBuilder`. The sign-in methods and `WithSessionTimeout` are listed in [Sign-in](#sign-in).

| Member | Purpose |
| --- | --- |
| `SetBasePath(path)` | Set the dashboard URL path. |
| `SetBackendDomain(domain)` | Set the backend API domain. |
| `SetCorsPolicy(Action<CorsPolicyBuilder> policy)` | Configure CORS for the dashboard endpoints (see [CORS](#cors)). |
| `SetGroupName(name)` | Set the OpenAPI group name for dashboard endpoints. |
| `DisableTickerQPathRedirect()` | Stop redirecting the old `/tickerq/dashboard` address. |
| `PreDashboardMiddleware` | `Action<IApplicationBuilder>` run before the dashboard pipeline. |
| `CustomMiddleware` | `Action<IApplicationBuilder>` added to the dashboard pipeline. |
| `PostDashboardMiddleware` | `Action<IApplicationBuilder>` run after the dashboard pipeline. |

Call `SetGroupName("tickernet")` to put the dashboard endpoints in their own OpenAPI group.

## Coming from TickerQ

TickerQ served the dashboard at `/tickerq/dashboard`. TickerNet redirects GET and HEAD requests for that address, and anything under it, to the same page under the dashboard's base path (`/tickernet/dashboard` by default). The rest of the path and the query string are kept, so old links and bookmarks still work. The redirect is temporary (HTTP 302), so browsers don't cache it. Turn it off with `DisableTickerQPathRedirect()`. It is never added when the base path is `/tickerq/dashboard` itself or sits under it (for example `/tickerq/dashboard/v2`).

Saved sign-ins and the chosen dashboard time zone also carry over. When the dashboard opens, it copies the old browser-storage keys (`tickerq_basic_auth`, `tickerq_api_key`, `tickerq_host_access_key`, `tickerq_bearer_token` and `tickerq:dashboard:timezone`) to their `tickernet` names, then removes the old keys. It copies a value only when the new key isn't set yet.

## Frontend integration

The web app adapts to the sign-in method you configured:

- Shows the matching login UI for basic, API key and host sign-in. Custom mode is for API clients only: the embedded app has no custom login form.
- Handles SignalR authentication.
- Supports both header and query parameter auth (for WebSockets).
