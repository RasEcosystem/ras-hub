using RasHub.Application.RasGates.Exceptions;

namespace RasHub.Infrastructure.RasGates.Rac.Adapters;

internal static class RacAdapterSelector
{
    public static T Resolve<T>(
        IEnumerable<T> adapters,
        string resource,
        string operation,
        Version racVersion)
        where T : IRacResourceAdapterDescriptor
    {
        var matchingAdapters = adapters.Where(adapter =>
            string.Equals(
                adapter.Resource,
                resource,
                StringComparison.Ordinal) &&
            string.Equals(
                adapter.Operation,
                operation,
                StringComparison.Ordinal));

        return SelectLatestCompatible(
            matchingAdapters,
            adapter => adapter.MinimumVersion,
            racVersion,
            () => new RasGateClientException(
                $"RAC version '{racVersion}' does not support " +
                $"'{resource}.{operation}'."),
            minimumVersion => new InvalidOperationException(
                $"Multiple RAC adapters handle '{resource}.{operation}' " +
                $"from minimum version '{minimumVersion}'."));
    }

    public static T SelectLatestCompatible<T>(
        IEnumerable<T> components,
        Func<T, Version> minimumVersion,
        Version racVersion,
        Func<Exception> unsupported,
        Func<Version, Exception> ambiguous)
    {
        return SelectLatest(
            components.Where(component =>
                minimumVersion(component) <= racVersion),
            minimumVersion,
            unsupported,
            ambiguous);
    }

    public static T SelectLatest<T>(
        IEnumerable<T> components,
        Func<T, Version> minimumVersion,
        Func<Exception> empty,
        Func<Version, Exception> ambiguous)
    {
        var candidates = components
            .OrderByDescending(minimumVersion)
            .Take(2)
            .ToArray();

        if (candidates.Length == 0)
            throw empty();

        var selectedMinimumVersion = minimumVersion(candidates[0]);
        if (candidates.Length > 1 &&
            selectedMinimumVersion == minimumVersion(candidates[1]))
            throw ambiguous(selectedMinimumVersion);

        return candidates[0];
    }
}

internal static class RacExecutionGuard
{
    public static void EnsureSucceeded(
        Version racVersion,
        Version minimumVersion,
        RacExecutionResult execution,
        string operationName)
    {
        if (racVersion < minimumVersion)
            throw new ArgumentOutOfRangeException(
                nameof(racVersion),
                racVersion,
                "The RAC version is not supported by this adapter.");

        if (execution.Outcome == RacExecutionOutcome.Unknown)
            throw new RasGateClientException(
                $"RAC {operationName} command outcome is unknown.");

        if (execution.Outcome == RacExecutionOutcome.Failed)
        {
            if (execution.TimedOut || execution.ExitCode == 0)
                throw new RasGateClientException(
                    $"RAC {operationName} command returned an inconsistent result.");

            throw new RasGateClientException(
                $"RAC {operationName} command failed with exit code " +
                $"{execution.ExitCode}.");
        }

        if (execution.Outcome != RacExecutionOutcome.Succeeded ||
            execution.TimedOut ||
            execution.ExitCode != 0)
            throw new RasGateClientException(
                $"RAC {operationName} command returned an inconsistent result.");
    }
}