using System.Text.Json;

namespace RasHub.Web.IntegrationTests.Api;

internal static class ApiResponseTestHelpers
{
    public static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);

        return document.RootElement.Clone();
    }

    public static string? GetErrorCode(JsonElement json)
    {
        return json
            .GetProperty("error")
            .GetProperty("code")
            .GetString();
    }

    public static void AssertTraceId(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("X-Trace-Id", out var values));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(values)));
    }
}