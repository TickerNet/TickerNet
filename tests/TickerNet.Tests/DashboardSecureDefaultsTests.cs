using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerNet.Dashboard;
using TickerNet.Dashboard.Authentication;
using TickerNet.Dashboard.DependencyInjection;
using TickerNet.DependencyInjection;

namespace TickerNet.Tests;

/// <summary>
/// The dashboard warns when it is open to anyone, accepts only same-origin requests unless told otherwise, compares
/// credentials in constant time and rejects a session timeout it can't honour.
/// </summary>
public class DashboardSecureDefaultsTests
{
    private const string OtherOrigin = "https://other.example";
    private const string AuthInfoPath = "/tickernet/dashboard/api/auth/info";

    [Fact]
    public async Task NoSignIn_NotConfirmed_LogsWarning()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await CreateDashboardHost(_ => { }, logs);

        Assert.Contains(logs.Warnings, m => m.Contains("has no sign-in"));
    }

    [Fact]
    public async Task NoSignIn_ConfirmedWithNoAuth_DoesNotWarn()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await CreateDashboardHost(d => d.WithNoAuth(), logs);

        Assert.DoesNotContain(logs.Warnings, m => m.Contains("has no sign-in"));
    }

    [Fact]
    public async Task SignInMethodSet_DoesNotWarn()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await CreateDashboardHost(d => d.WithBasicAuth("admin", "secret"), logs);

        Assert.DoesNotContain(logs.Warnings, m => m.Contains("has no sign-in"));
    }

    [Fact]
    public async Task DefaultCors_DoesNotAllowOtherOrigins()
    {
        using var host = await CreateDashboardHost(d => d.WithNoAuth(), new CapturingLoggerProvider());

        var response = await host.GetTestClient().SendAsync(Preflight(AuthInfoPath));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task DefaultCors_SameOriginRequestsStillWork()
    {
        using var host = await CreateDashboardHost(d => d.WithNoAuth(), new CapturingLoggerProvider());

        var response = await host.GetTestClient().GetAsync(AuthInfoPath);

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task BackendDomainWithoutPolicy_KeepsPermissiveCors_AndWarns()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await CreateDashboardHost(d =>
        {
            d.WithNoAuth();
            d.SetBackendDomain("https://jobs.example");
        }, logs);

        var response = await host.GetTestClient().SendAsync(Preflight(AuthInfoPath));

        Assert.Equal(OtherOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains(logs.Warnings, m => m.Contains("accepts requests with credentials from any website"));
    }

    [Fact]
    public async Task CustomCorsPolicy_IsUsed_AndDoesNotWarn()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await CreateDashboardHost(d =>
        {
            d.WithNoAuth();
            d.SetBackendDomain("https://jobs.example");
            d.SetCorsPolicy(cors => cors.WithOrigins(OtherOrigin).AllowAnyHeader());
        }, logs);

        var response = await host.GetTestClient().SendAsync(Preflight(AuthInfoPath));

        Assert.Equal(OtherOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.DoesNotContain(logs.Warnings, m => m.Contains("from any website"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void WithSessionTimeout_BelowOneMinute_Throws(int minutes)
    {
        var builder = new DashboardOptionsBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithSessionTimeout(minutes));
    }

    [Fact]
    public void WithSessionTimeout_SetsMinutes()
    {
        var builder = new DashboardOptionsBuilder();

        builder.WithSessionTimeout(15);

        Assert.Equal(15, builder.Auth.SessionTimeoutMinutes);
    }

    [Theory]
    [InlineData("admin:secret", true)]
    [InlineData("admin:secreT", false)]
    [InlineData("admin:secret2", false)]
    [InlineData("admin:", false)]
    public async Task BasicAuth_AcceptsOnlyTheConfiguredCredentials(string credentials, bool expected)
    {
        var builder = new DashboardOptionsBuilder().WithBasicAuth("admin", "secret");
        var service = new AuthService(builder.Auth, Substitute.For<ILogger<AuthService>>());
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));

        var result = await service.AuthenticateAsync(context);

        Assert.Equal(expected, result.IsAuthenticated);
    }

    [Theory]
    [InlineData("Bearer my-key", true)]
    [InlineData("Bearer:my-key", true)]
    [InlineData("Bearer my-kez", false)]
    [InlineData("Bearer my-key-longer", false)]
    public async Task ApiKey_AcceptsOnlyTheConfiguredKey(string header, bool expected)
    {
        var builder = new DashboardOptionsBuilder().WithApiKey("my-key");
        var service = new AuthService(builder.Auth, Substitute.For<ILogger<AuthService>>());
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = header;

        var result = await service.AuthenticateAsync(context);

        Assert.Equal(expected, result.IsAuthenticated);
    }

    private static HttpRequestMessage Preflight(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", OtherOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return request;
    }

    private static async Task<IHost> CreateDashboardHost(Action<DashboardOptionsBuilder> configure, CapturingLoggerProvider logs)
    {
        return await new HostBuilder()
            .ConfigureLogging(logging => logging.AddProvider(logs))
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddTickerNet(options => options.AddDashboard(configure));
                });
                webBuilder.Configure(app =>
                {
                    app.Run(context => context.Response.WriteAsync("fallthrough"));
                });
            })
            .StartAsync();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public IReadOnlyCollection<string> Warnings => _warnings.ToArray();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                    owner._warnings.Enqueue(formatter(state, exception));
            }
        }
    }
}
