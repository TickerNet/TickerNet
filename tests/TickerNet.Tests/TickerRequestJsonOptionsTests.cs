using System.Text.Json.Serialization.Metadata;
using TickerNet.Dashboard.Infrastructure.Dashboard;
using TickerNet.Utilities;
using Xunit;

namespace TickerNet.Tests;

/// <summary>
/// The request options must be usable before a host configures a JSON context:
/// the dashboard asks for an example of a function's request type during startup.
/// </summary>
public class TickerRequestJsonOptionsTests
{
    private sealed class SampleRequest
    {
        public string Name { get; set; } = "sample";
        public int Count { get; set; } = 3;
    }

    [Fact]
    public void DefaultOptions_ResolveATypeWithoutAHostContext()
    {
        Assert.NotNull(TickerHelper.RequestJsonSerializerOptions.TypeInfoResolver);
        var info = TickerHelper.RequestJsonSerializerOptions.GetTypeInfo(typeof(SampleRequest));
        Assert.Equal(typeof(SampleRequest), info.Type);
    }

    [Fact]
    public void ExampleGenerator_DoesNotCacheAFailureWhenOptionsGainAResolver()
    {
        var original = TickerHelper.RequestJsonSerializerOptions;
        try
        {
            // No resolver: GetTypeInfo cannot answer.
            TickerHelper.RequestJsonSerializerOptions = new System.Text.Json.JsonSerializerOptions();
            Assert.False(JsonExampleGenerator.TryGenerateExampleJson(typeof(SampleRequest), out var missing));
            Assert.Equal(string.Empty, missing);

            // The same type succeeds once options can resolve it; a cached failure
            // would keep the dashboard field disabled for the rest of the process.
            TickerHelper.RequestJsonSerializerOptions = new System.Text.Json.JsonSerializerOptions
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            };
            Assert.True(JsonExampleGenerator.TryGenerateExampleJson(typeof(SampleRequest), out var example));
            Assert.Contains("\"Name\"", example);
            Assert.Contains("\"Count\"", example);
        }
        finally
        {
            TickerHelper.RequestJsonSerializerOptions = original;
        }
    }
}
