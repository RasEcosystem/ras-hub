using RasHub.Web.Infrastructure.Logging;

namespace RasHub.Web.IntegrationTests.Infrastructure;

public sealed class SwaggerUiUrlResolverTests
{
    [Theory]
    [InlineData("http://localhost:5076/", "http://localhost:5076/swagger/")]
    [InlineData("https://rashub.example.com/app/", "https://rashub.example.com/app/swagger/")]
    [InlineData(
        "http://192.168.253.48:5076/rashub/",
        "http://192.168.253.48:5076/rashub/swagger/")]
    public void Resolve_uses_the_current_application_origin_and_path_base(
        string applicationBaseUrl,
        string expected)
    {
        var result = SwaggerUiUrlResolver.Resolve(new Uri(applicationBaseUrl));

        Assert.Equal(expected, result);
    }
}