using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TickerNet")]
[assembly: InternalsVisibleTo("TickerNet.EntityFrameworkCore")]
[assembly: InternalsVisibleTo("TickerNet.MongoDB")]
[assembly: InternalsVisibleTo("TickerNet.Dashboard")]
[assembly: InternalsVisibleTo("TickerNet.Tests")]
[assembly: InternalsVisibleTo("TickerNet.Instrumentation.OpenTelemetry")]
[assembly: InternalsVisibleTo("TickerNet.Caching.StackExchangeRedis")]
[assembly: InternalsVisibleTo("TickerNet.EntityFrameworkCore.Tests")]
[assembly: InternalsVisibleTo("TickerNet.Caching.StackExchangeRedis.Tests")]
[assembly: InternalsVisibleTo("TickerNet.MongoDB.Tests")]
// To be testable using NSubsitute
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]