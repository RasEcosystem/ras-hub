namespace RasHub.Application.RasGates.Abstractions;

public interface IRasGateEndpointFactory
{
    Uri CreateBaseAddress(string url, int port);
}
