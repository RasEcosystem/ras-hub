using System.Text.Json;
using System.Text.Json.Serialization;

namespace RasHub.Web.Api;

public static class ApiJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(
            null,
            false));
    }
}
