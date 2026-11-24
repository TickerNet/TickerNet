using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerNet.Dashboard.DependencyInjection;
using TickerNet.DependencyInjection;

namespace TickerNet.Tests;

/// <summary>
/// The dashboard's old TickerQ address (/tickerq/dashboard) redirects to the current base path,
/// so links and bookmarks made with TickerQ keep working.
/// </summary>
public class DashboardTickerQPathRedirectTests
{
    [Theory]
    [InlineData("/tickerq/dashboard", "/tickernet/dashboard")]
    [InlineData("/tickerq/dashboard/", "/tickernet/dashboard/")]
    [InlineData("/tickerq/dashboard/time-tickers", "/tickernet/dashboard/time-tickers")]
    [InlineData("/tickerq/dashboard/cron-tickers?page=2&size=50", "/tickernet/dashboard/cron-tickers?page=2&size=50")]
    [InlineData("/TickerQ/Dashboard/time-tickers", "/tickernet/dashboard/time-tickers")]
    public async Task Get_OldPath_RedirectsToBasePath(string requestPath, string expectedLocation)
    {
        using var host = await CreateRedirectHost("/tickernet/dashboard", usePathBase: null);
        var client = host.GetTestClient();

        var response = await client.GetAsync(requestPath);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Head_OldPath_Redirects()
    {
        using var host = await CreateRedirectHost("/tickernet/dashboard", usePathBase: null);
        var client = host.GetTestClient();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/tickerq/dashboard/time-tickers"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/tickernet/dashboard/time-tickers", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Post_OldPath_IsNotRedirected()
    {
        using var host = await CreateRedirectHost("/tickernet/dashboard", usePathBase: null);
        var client = host.GetTestClient();

        var response = await client.PostAsync("/tickerq/dashboard/api/time-tickers", new StringContent(""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("fallthrough", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/tickerq/dashboards")]
    [InlineData("/tickerq")]
    [InlineData("/tickernet/dashboard")]
    public async Task Get_OtherPaths_AreNotRedirected(string requestPath)
    {
        using var host = await CreateRedirectHost("/tickernet/dashboard", usePathBase: null);
        var client = host.GetTestClient();

        var response = await client.GetAsync(requestPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("fallthrough", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_OldPath_CustomBasePath_RedirectsToIt()
    {
        using var host = await CreateRedirectHost("/admin/jobs", usePathBase: null);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/tickerq/dashboard/time-tickers");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/admin/jobs/time-tickers", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/tickerq/dashboard//other.example", "/other.example")]
    [InlineData("/tickerq/dashboard///other.example/path", "/other.example/path")]
    [InlineData("/tickerq/dashboard/%5Cother.example", "/other.example")]
    [InlineData("/tickerq/dashboard/%5C%5Cother.example", "/other.example")]
    public async Task Get_OldPath_RootBasePath_NeverRedirectsOffSite(string requestPath, string expectedLocation)
    {
        using var host = await CreateRedirectHost("/", usePathBase: null);
        var client = host.GetTestClient();

        var response = await client.GetAsync(requestPath);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Get_OldPath_RootBasePath_RedirectsToRoot()
    {
        using var host = await CreateRedirectHost("/", usePathBase: null);
        var client = host.GetTestClient();

        var root = await client.GetAsync("/tickerq/dashboard");
        var page = await client.GetAsync("/tickerq/dashboard/time-tickers");

        Assert.Equal("/", root.Headers.Location?.OriginalString);
        Assert.Equal("/time-tickers", page.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Get_OldPath_WithPathBase_KeepsPathBase()
    {
        using var host = await CreateRedirectHost("/tickernet/dashboard", usePathBase: "/cool-app");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/cool-app/tickerq/dashboard/time-tickers");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/cool-app/tickernet/dashboard/time-tickers", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/tickerq/dashboard")]
    [InlineData("/tickerq/dashboard/v2")]
    public async Task BasePathIsOldPathOrUnderIt_NoRedirect(string basePath)
    {
        using var host = await CreateRedirectHost(basePath, usePathBase: null);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/tickerq/dashboard/v2/time-tickers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("fallthrough", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dashboard_RedirectsOldPathByDefault()
    {
        using var host = await CreateDashboardHost(configure: null);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/tickerq/dashboard/time-tickers?x=1");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/tickernet/dashboard/time-tickers?x=1", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Dashboard_DisableTickerQPathRedirect_TurnsItOff()
    {
        using var host = await CreateDashboardHost(dashboard => dashboard.DisableTickerQPathRedirect());
        var client = host.GetTestClient();

        var response = await client.GetAsync("/tickerq/dashboard/time-tickers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("fallthrough", await response.Content.ReadAsStringAsync());
    }

    private static async Task<IHost> CreateRedirectHost(string basePath, string? usePathBase)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.Configure(app =>
                {
                    if (usePathBase != null)
                        app.UsePathBase(usePathBase);

                    app.UseTickerQPathRedirect(basePath);
                    app.Run(context => context.Response.WriteAsync("fallthrough"));
                });
            })
            .StartAsync();
    }

    private static async Task<IHost> CreateDashboardHost(Action<TickerNet.Dashboard.DashboardOptionsBuilder>? configure)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddTickerNet(options =>
                    {
                        options.AddDashboard(dashboard =>
                        {
                            dashboard.WithNoAuth();
                            configure?.Invoke(dashboard);
                        });
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.Run(context => context.Response.WriteAsync("fallthrough"));
                });
            })
            .StartAsync();
    }
}
