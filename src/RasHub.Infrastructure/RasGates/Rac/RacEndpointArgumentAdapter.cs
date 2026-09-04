using RasHub.Application.RasEndpoints.Models;

namespace RasHub.Infrastructure.RasGates.Rac;

internal sealed class RacEndpointArgumentAdapter
{
    public IReadOnlyList<string> Apply(
        IReadOnlyList<string> arguments,
        RasEndpointAddress address)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(address);

        if (arguments.Count == 0)
            throw new ArgumentException(
                "A RAC command is required before the RAS endpoint.",
                nameof(arguments));

        return [.. arguments, address.ToString()];
    }
}
