namespace RasHub.Synchronization.Internal;

internal static class BackgroundTaskOptionsValidator
{
    public static void Validate(BackgroundTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Enum.IsDefined(options.Queue))
            throw new ArgumentOutOfRangeException(nameof(options.Queue));

        if (options.MaxAttempts < 1)
            throw new ArgumentOutOfRangeException(
                nameof(options.MaxAttempts),
                "Maximum attempts must be greater than zero.");

        if (options.RetryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.RetryDelay));

        if (options.RetryBackoffFactor < 1 ||
            double.IsNaN(options.RetryBackoffFactor) ||
            double.IsInfinity(options.RetryBackoffFactor))
            throw new ArgumentOutOfRangeException(
                nameof(options.RetryBackoffFactor),
                "Retry backoff factor must be finite and at least one.");

        if (options.MaxRetryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.MaxRetryDelay));

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.Timeout));

        ValidateKey(options.DeduplicationKey, nameof(options.DeduplicationKey));
        ValidateKey(options.ConcurrencyKey, nameof(options.ConcurrencyKey));
    }

    private static void ValidateKey(string? value, string parameterName)
    {
        if (value is null)
            return;

        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
            throw new ArgumentException(
                "Keys must contain between 1 and 512 characters.",
                parameterName);
    }
}