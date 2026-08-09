using RasHub.Domain;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasGateClientFactory
{
    IRasGateClient Create(RasGate rasGate);
}