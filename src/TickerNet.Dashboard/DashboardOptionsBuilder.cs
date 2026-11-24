using System;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using TickerNet.Dashboard.Authentication;

namespace TickerNet.Dashboard;

public class DashboardOptionsBuilder
{
    internal string BasePath { get; set; } = "/tickernet/dashboard";
    internal Action<CorsPolicyBuilder> CorsPolicyBuilder { get; set; }
    internal string BackendDomain { get; set; }
    internal string GroupName { get; set; }
    
    // Clean authentication system
    internal AuthConfig Auth { get; set; } = new();
    
    // Custom Middleware Integration
    public Action<IApplicationBuilder> CustomMiddleware { get; set; }
    public Action<IApplicationBuilder> PreDashboardMiddleware { get; set; }
    public Action<IApplicationBuilder> PostDashboardMiddleware { get; set; }
    
    internal JsonSerializerOptions DashboardJsonOptions { get; set; }

    /// <summary>Tracks whether dashboard middleware has been applied to prevent double registration.</summary>
    internal bool MiddlewareApplied { get; set; }

    /// <summary>
    /// Redirect the dashboard's old TickerQ address (<c>/tickerq/dashboard</c>) to <see cref="BasePath"/>.
    /// On by default; turned off with <see cref="DisableTickerQPathRedirect"/>.
    /// </summary>
    internal bool RedirectTickerQPath { get; set; } = true;

    /// <summary>True when the app called <see cref="WithNoAuth"/> to confirm a dashboard without sign-in.</summary>
    internal bool NoAuthConfirmed { get; private set; }

    /// <summary>True when the app set its own CORS policy with <see cref="SetCorsPolicy"/>.</summary>
    internal bool CorsPolicyConfigured { get; private set; }

    /// <summary>
    /// Set the CORS policy for the dashboard's endpoints. Without one, the dashboard accepts only same-origin
    /// requests, unless <see cref="SetBackendDomain"/> is used, in which case any origin is allowed (with a startup
    /// warning) so a separately hosted front end keeps working.
    /// </summary>
    public void SetCorsPolicy(Action<CorsPolicyBuilder> corsPolicyBuilder)
    {
        CorsPolicyBuilder = corsPolicyBuilder;
        CorsPolicyConfigured = corsPolicyBuilder != null;
    }
    
    public void SetBasePath(string basePath)
        => BasePath = basePath;
    
    public void SetBackendDomain(string backendDomain)
        => BackendDomain = backendDomain;

    /// <summary>
    /// Stop redirecting the dashboard's old TickerQ address. By default, GET and HEAD requests to
    /// <c>/tickerq/dashboard</c> and anything under it get a temporary redirect (302) to the same page under the
    /// dashboard's base path, so links and bookmarks from TickerQ keep working. The redirect is never added when
    /// the base path is <c>/tickerq/dashboard</c> or sits under it.
    /// </summary>
    public DashboardOptionsBuilder DisableTickerQPathRedirect()
    {
        RedirectTickerQPath = false;
        return this;
    }

    /// <summary>Set OpenAPI group name for dashboard endpoints</summary>
    public void SetGroupName(string groupName)
        => GroupName = groupName;
    
    /// <summary>
    /// Confirm that the dashboard has no sign-in, so anyone who can reach it can view, add, run and delete jobs.
    /// Without a sign-in method or this call, TickerNet logs a warning at startup.
    /// </summary>
    public DashboardOptionsBuilder WithNoAuth()
    {
        Auth.Mode = AuthMode.None;
        NoAuthConfirmed = true;
        return this;
    }

    /// <summary>Enable Basic Authentication with username/password</summary>
    public DashboardOptionsBuilder WithBasicAuth(string username, string password)
    {
        Auth.Mode = AuthMode.Basic;
        Auth.BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return this;
    }
    
    /// <summary>Enable API Key authentication (sent as Bearer token)</summary>
    public DashboardOptionsBuilder WithApiKey(string apiKey)
    {
        Auth.Mode = AuthMode.ApiKey;
        Auth.ApiKey = apiKey;
        return this;
    }
    
    /// <summary>Use the host application's existing authentication system</summary>
    /// <param name="policy">Optional authorization policy name to require (e.g., "AdminPolicy"). If null or empty, uses the default policy.</param>
    public DashboardOptionsBuilder WithHostAuthentication(string? policy = null)
    {
        Auth.Mode = AuthMode.Host;
        Auth.HostAuthorizationPolicy = policy;
        return this;
    }
    
    /// <summary>Configure custom authentication with validation function</summary>
    public DashboardOptionsBuilder WithCustomAuth(Func<string, bool> validator)
    {
        Auth.Mode = AuthMode.Custom;
        Auth.CustomValidator = validator;
        return this;
    }
    
    /// <summary>
    /// How long a sign-in lasts in the browser, in minutes (default 60). When it runs out, the dashboard forgets the
    /// stored credentials and asks for them again, including in a tab that is still open.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minutes"/> is less than 1.</exception>
    public DashboardOptionsBuilder WithSessionTimeout(int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minutes, 1);
        Auth.SessionTimeoutMinutes = minutes;
        return this;
    }
    
    /// <summary>Validate the authentication configuration</summary>
    internal void Validate()
    {
        Auth.Validate();
    }
}
