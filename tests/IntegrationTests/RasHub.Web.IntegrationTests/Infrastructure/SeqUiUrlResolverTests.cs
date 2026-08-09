using RasHub.Web.Infrastructure.Logging;

namespace RasHub.Web.IntegrationTests.Infrastructure;

public sealed class SeqUiUrlResolverTests
{
    [Theory]
    [InlineData(
        "http://localhost:5341",
        "http://192.168.253.48:5076/",
        "http://192.168.253.48:5341/")]
    [InlineData(
        "http://seq:5341",
        "http://localhost:5076/",
        "http://localhost:5341/")]
    [InlineData(
        "https://seq.example.com/logs",
        "https://rashub.example.com/",
        "https://seq.example.com/logs")]
    [InlineData(
        "/seq/",
        "https://rashub.example.com/app/",
        "https://rashub.example.com/seq/")]
    public void Resolve_returns_a_browser_accessible_url(
        string configuredUrl,
        string applicationBaseUrl,
        string expected)
    {
        var result = SeqUiUrlResolver.Resolve(
            configuredUrl,
            new Uri(applicationBaseUrl));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    public void Resolve_rejects_missing_or_unsafe_urls(string? configuredUrl)
    {
        Assert.Null(SeqUiUrlResolver.Resolve(
            configuredUrl,
            new Uri("https://rashub.example.com/")));
    }
}