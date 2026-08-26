namespace RasHub.Infrastructure.RasGates.Rac;

public enum RacExecutionOutcome
{
    Succeeded,
    Failed,
    Unknown
}

public sealed record RacExecutionResult
{
    public required RacExecutionOutcome Outcome { get; init; }

    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public required long DurationMilliseconds { get; init; }

    public required bool TimedOut { get; init; }
}
