using RasHub.Application.RasEndpoints.Models;
using RasHub.Infrastructure.RasGates.Rac;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac;

public sealed class RacEndpointArgumentAdapterTests
{
    [Fact]
    public void Apply_appends_endpoint_without_mutating_command_arguments()
    {
        string[] arguments = ["cluster", "list"];
        var adapter = new RacEndpointArgumentAdapter();

        var result = adapter.Apply(
            arguments,
            RasEndpointAddress.Create("RAS.EXAMPLE.TEST.", 1545));

        Assert.Equal(["cluster", "list"], arguments);
        Assert.Equal(
            ["cluster", "list", "ras.example.test:1545"],
            result);
    }

    [Fact]
    public void Apply_formats_IPv6_endpoint_as_bracketed_host_and_port()
    {
        var adapter = new RacEndpointArgumentAdapter();

        var result = adapter.Apply(
            ["cluster", "list"],
            RasEndpointAddress.Create("2001:0db8::1", 1545));

        Assert.Equal("[2001:db8::1]:1545", result[^1]);
    }

    [Fact]
    public void Apply_rejects_missing_RAC_command()
    {
        var adapter = new RacEndpointArgumentAdapter();

        Assert.Throws<ArgumentException>(() => adapter.Apply(
            [],
            RasEndpointAddress.Create("ras.example.test", 1545)));
    }
}
