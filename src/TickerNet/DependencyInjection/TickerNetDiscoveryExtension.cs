using System.Reflection;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;

namespace TickerNet.DependencyInjection;

public static class TickerNetDiscoveryExtension
{
    private const string GeneratedClassSuffix = "TickerNetInstanceFactoryExtensions";
    
    /// <summary>
    /// Loads the assemblies to initialize the source generated code.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerNetDiscovery<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, 
        Assembly[] assemblies)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var assembliesToLoad = assemblies ?? [];

        foreach (var assembly in assembliesToLoad)
        {
            if(!string.IsNullOrEmpty(assembly.FullName))
                Assembly.Load(assembly.FullName);
        }
        
        return tickerConfiguration;
    }
}