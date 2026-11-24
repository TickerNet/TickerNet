using System.Reflection;
using TickerNet.Utilities;

namespace TickerNet.Tests;

public class FunctionRegistryImmutabilityTests
{
    [Theory]
    [InlineData(nameof(TickerFunctionProvider.TickerFunctions))]
    [InlineData(nameof(TickerFunctionProvider.TickerFunctionRequestTypes))]
    [InlineData(nameof(TickerFunctionProvider.TickerFunctionRequestInfos))]
    public void Registry_is_a_property_without_a_public_setter(string name)
    {
        var property = typeof(TickerFunctionProvider).GetProperty(name, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(property);
        Assert.True(property.GetMethod!.IsPublic);
        Assert.NotNull(property.SetMethod);
        Assert.True(property.SetMethod!.IsPrivate);
    }
}
