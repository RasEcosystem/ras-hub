using RasHub.Application.RasGates.Exceptions;
using RasHub.Infrastructure.RasGates.Endpoints;

namespace RasHub.Infrastructure.UnitTests.RasGates.Endpoints;

public sealed class RasGateEndpointFactoryTests
{
    [Theory]
    [InlineData(
        "http://127.0.0.1",
        5050,
        "http://127.0.0.1:5050/")]
    [InlineData(
        "http://192.168.253.45/root",
        8080,
        "http://192.168.253.45:8080/root/")]
    [InlineData(
        "https://gate.example.test",
        443,
        "https://gate.example.test/")]
    [InlineData(
        "https://169.254.169.254",
        8443,
        "https://169.254.169.254:8443/")]
    public void CreateBaseAddress_supported_endpoint_returns_normalized_address(
        string url,
        int port,
        string expected)
    {
        var factory = new RasGateEndpointFactory();

        var result = factory.CreateBaseAddress(url, port);

        Assert.Equal(expected, result.AbsoluteUri);
    }

    [Theory]
    [InlineData("gate.example.test", 443)]
    [InlineData("ftp://gate.example.test", 21)]
    [InlineData("https://user@gate.example.test", 443)]
    [InlineData("https://gate.example.test?query=value", 443)]
    [InlineData("https://gate.example.test#fragment", 443)]
    [InlineData("https://gate.example.test:8443", 443)]
    [InlineData("https://gate.example.test", 0)]
    [InlineData("https://gate.example.test", 65_536)]
    public void CreateBaseAddress_invalid_endpoint_throws_validation_exception(
        string url,
        int port)
    {
        var factory = new RasGateEndpointFactory();

        Assert.Throws<RasGateEndpointValidationException>(() =>
            factory.CreateBaseAddress(url, port));
    }
}
