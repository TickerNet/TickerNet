using System.Text.Json.Serialization;

namespace TickerNet.Exceptions
{
    [JsonSerializable(typeof(ExceptionDetailClassForSerialization))]
    internal partial class TickerNetInternalJsonContext : JsonSerializerContext { }
}
