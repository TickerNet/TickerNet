using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using TickerNet.Dashboard.DependencyInjection;
using TickerNet.Utilities.Entities;

namespace TickerNet.Dashboard.Infrastructure;

internal class DashboardStartupFilter<TTimeTicker, TCronTicker> : IStartupFilter
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly DashboardOptionsBuilder _config;

    public DashboardStartupFilter(DashboardOptionsBuilder config)
    {
        _config = config;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Only apply if not already applied by UseDashboardDelegate (new WebApplication pattern)
            if (!_config.MiddlewareApplied)
            {
                _config.MiddlewareApplied = true;
                app.UseDashboardWithEndpoints<TTimeTicker, TCronTicker>(_config);
            }

            next(app);
        };
    }
}
