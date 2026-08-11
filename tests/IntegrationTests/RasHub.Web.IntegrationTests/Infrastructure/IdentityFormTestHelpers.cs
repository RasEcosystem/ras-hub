using System.Net;
using System.Text.RegularExpressions;

namespace RasHub.Web.IntegrationTests.Infrastructure;

internal static partial class IdentityFormTestHelpers
{
    public static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client,
        string path)
    {
        using var page = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var token = AntiforgeryTokenRegex()
            .Match(html)
            .Groups[1]
            .Value;

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.False(string.IsNullOrEmpty(token));

        return WebUtility.HtmlDecode(token);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenRegex();
}