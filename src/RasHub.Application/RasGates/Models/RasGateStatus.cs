namespace RasHub.Application.RasGates.Models;

public sealed record RasGateStatus(
    string InstanceName,
    string Version);