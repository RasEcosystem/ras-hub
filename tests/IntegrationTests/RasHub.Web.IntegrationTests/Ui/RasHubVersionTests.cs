using System.Reflection;

namespace RasHub.Web.IntegrationTests.Ui;

public sealed class RasHubVersionTests
{
    private static readonly MethodInfo GetDisplayVersion = typeof(Program).Assembly
        .GetType("RasHub.Web.RasHubVersion", true)!
        .GetMethod(
            "GetDisplayVersion",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Theory]
    [InlineData("0.1.0-g676e1c91e7", "0.1.0")]
    [InlineData("0.1.0-beta.1.g676e1c91e7", "0.1.0-beta.1")]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0-beta.1", "0.1.0-beta.1")]
    [InlineData("0.1.0-gnot-a-revision", "0.1.0-gnot-a-revision")]
    public void GetDisplayVersion_version_expected_display_version(
        string version,
        string expected)
    {
        Assert.Equal(expected, GetDisplayVersion.Invoke(null, [version]));
    }
}
