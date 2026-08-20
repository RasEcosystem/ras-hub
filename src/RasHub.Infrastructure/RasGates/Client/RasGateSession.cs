using System.Net.Http.Json;
using System.Text.Json;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.RasGates.Client;

internal sealed class RasGateSession
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _apiKey;
    private readonly Uri _baseAddress;
    private readonly RacCapabilityResolver _capabilityResolver;
    private readonly long _configurationRevision;
    private readonly HttpClient _httpClient;
    private readonly RacVersionCache _racVersionCache;
    private readonly Guid _rasGateId;
    private readonly RacVersionParser _versionParser;
    private Version? _racVersion;

    public RasGateSession(
        HttpClient httpClient,
        Uri baseAddress,
        string apiKey,
        Guid rasGateId,
        long configurationRevision,
        RacVersionCache racVersionCache,
        RacVersionParser versionParser,
        RacCapabilityResolver capabilityResolver)
    {
        _httpClient = httpClient;
        _baseAddress = baseAddress;
        _apiKey = apiKey;
        _rasGateId = rasGateId;
        _configurationRevision = configurationRevision;
        _racVersionCache = racVersionCache;
        _versionParser = versionParser;
        _capabilityResolver = capabilityResolver;
    }

    public async Task<RasGateStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            "rasgate/status");
        var data = await SendAsync<RasGateStatusData>(request, cancellationToken);

        if (string.IsNullOrWhiteSpace(data.InstanceName) ||
            string.IsNullOrWhiteSpace(data.Version))
            throw new RasGateClientException(
                "RasGate returned an incomplete status response.");

        return new RasGateStatus(
            data.InstanceName,
            data.Version);
    }

    public async Task<RasGateCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken)
    {
        var racVersion = await GetRacVersionAsync(cancellationToken);

        return new RasGateCapabilities
        {
            RacVersion = racVersion.ToString(),
            Resources = _capabilityResolver.GetCapabilities(racVersion)
        };
    }

    public async Task<Version> GetRacVersionAsync(
        CancellationToken cancellationToken)
    {
        if (_racVersion is not null)
            return _racVersion;

        if (_racVersionCache.TryGet(
                _rasGateId,
                _configurationRevision,
                out var cachedVersion))
        {
            _racVersion = cachedVersion;
            return cachedVersion;
        }

        using var request = CreateRequest(HttpMethod.Get, "rac/status");
        var data = await SendAsync<RacStatusData>(request, cancellationToken);

        if (!data.Available || string.IsNullOrWhiteSpace(data.Version))
            throw new RasGateClientException("RAC is unavailable through RasGate.");

        _racVersion = _versionParser.Parse(data.Version);
        _racVersionCache.Set(
            _rasGateId,
            _configurationRevision,
            _racVersion);
        return _racVersion;
    }

    public T ParseRacOutput<T>(Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (RacOutputDeserializationException)
        {
            InvalidateRacVersion();
            throw;
        }
        catch (RasGateClientException exception)
            when (exception.InnerException is RacOutputDeserializationException)
        {
            InvalidateRacVersion();
            throw;
        }
    }

    public Task<RacExecutionResult> ExecuteRacQueryAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return ExecuteRacAsync(arguments, null, cancellationToken);
    }

    public Task<RacExecutionResult> ExecuteRacMutationAsync(
        IReadOnlyList<string> arguments,
        string resource,
        string operation,
        CancellationToken cancellationToken)
    {
        return ExecuteRacAsync(
            arguments,
            new RacMutation(resource, operation),
            cancellationToken);
    }

    private void InvalidateRacVersion()
    {
        _racVersion = null;
        _racVersionCache.Remove(_rasGateId, _configurationRevision);
    }

    private async Task<RacExecutionResult> ExecuteRacAsync(
        IReadOnlyList<string> arguments,
        RacMutation? mutation,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "rac/execute",
            true);
        request.Content = JsonContent.Create(
            new ExecuteRacRequest
            {
                Arguments = arguments
            },
            options: JsonOptions);

        var data = await SendAsync<ExecuteRacData>(
            request,
            cancellationToken,
            mutation);

        if (data.ExitCode is null ||
            data.TimedOut is null ||
            data.StandardOutput is null ||
            data.StandardError is null ||
            data.DurationMilliseconds is null)
            throw new RasGateClientException(
                "RasGate returned an incomplete RAC execution response.");

        var outcome = ParseOutcome(
            data.Outcome,
            data.ExitCode.Value,
            data.TimedOut.Value);

        if (outcome == RacExecutionOutcome.Unknown &&
            mutation is { } unknownMutation)
            throw new RasGateMutationOutcomeUnknownException(
                _rasGateId,
                unknownMutation.Resource,
                unknownMutation.Operation);

        return new RacExecutionResult
        {
            Outcome = outcome,
            ExitCode = data.ExitCode.Value,
            StandardOutput = data.StandardOutput,
            StandardError = data.StandardError,
            DurationMilliseconds = data.DurationMilliseconds.Value,
            TimedOut = data.TimedOut.Value
        };
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativeUri,
        bool authenticate = false)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(_baseAddress, relativeUri));

        if (authenticate)
            request.Headers.Add(ApiKeyHeaderName, _apiKey);

        return request;
    }

    private async Task<T> SendAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        RacMutation? mutation = null)
        where T : class
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(
                request,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RasGateClientException("The RasGate request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new RasGateClientException(
                "RasGate could not be reached.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorCode = await ReadErrorCodeAsync(
                    response,
                    cancellationToken);

                if (mutation is { } unknownMutation &&
                    IsUnknownOutcomeErrorCode(errorCode))
                    throw new RasGateMutationOutcomeUnknownException(
                        _rasGateId,
                        unknownMutation.Resource,
                        unknownMutation.Operation);

                throw new RasGateClientException(
                    $"RasGate returned HTTP status code {(int)response.StatusCode}.");
            }

            RasGateApiResponse<T>? envelope;

            try
            {
                envelope = await response.Content.ReadFromJsonAsync<
                    RasGateApiResponse<T>>(
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new RasGateClientException(
                    "RasGate returned invalid JSON.",
                    exception);
            }
            catch (NotSupportedException exception)
            {
                throw new RasGateClientException(
                    "RasGate returned an unsupported response.",
                    exception);
            }

            if (envelope is null)
                throw new RasGateClientException(
                    "RasGate returned an empty response.");

            if (!envelope.Success)
            {
                if (mutation is { } unknownMutation &&
                    IsUnknownOutcomeErrorCode(envelope.Error?.Code))
                    throw new RasGateMutationOutcomeUnknownException(
                        _rasGateId,
                        unknownMutation.Resource,
                        unknownMutation.Operation);

                throw new RasGateClientException(
                    envelope.Error is null
                        ? "RasGate reported a request failure."
                        : $"RasGate reported error '{envelope.Error.Code}'.");
            }

            return envelope.Data ?? throw new RasGateClientException(
                "RasGate returned an incomplete response.");
        }
    }

    private static RacExecutionOutcome ParseOutcome(
        string? value,
        int exitCode,
        bool timedOut)
    {
        if (value is null)
            return timedOut
                ? RacExecutionOutcome.Unknown
                : exitCode == 0
                    ? RacExecutionOutcome.Succeeded
                    : RacExecutionOutcome.Failed;

        var outcome = value.ToLowerInvariant() switch
        {
            "succeeded" => RacExecutionOutcome.Succeeded,
            "failed" => RacExecutionOutcome.Failed,
            "unknown" => RacExecutionOutcome.Unknown,
            _ => throw new RasGateClientException(
                "RasGate returned an invalid RAC execution outcome.")
        };

        var succeededResultIsInconsistent =
            outcome == RacExecutionOutcome.Succeeded &&
            (timedOut || exitCode != 0);
        var failedResultIsInconsistent =
            outcome == RacExecutionOutcome.Failed &&
            (timedOut || exitCode == 0);

        if (succeededResultIsInconsistent || failedResultIsInconsistent)
            throw new RasGateClientException(
                "RasGate returned an inconsistent RAC execution response.");

        return outcome;
    }

    private static async Task<string?> ReadErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<
                RasGateApiResponse<object>>(
                JsonOptions,
                cancellationToken);

            return envelope?.Error?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsUnknownOutcomeErrorCode(string? errorCode)
    {
        return string.Equals(
                   errorCode,
                   "rac_execution_outcome_unknown",
                   StringComparison.Ordinal) ||
               string.Equals(
                   errorCode,
                   "rac_output_limit_exceeded",
                   StringComparison.Ordinal);
    }

    private sealed record ExecuteRacRequest
    {
        public required IReadOnlyList<string> Arguments { get; init; }
    }

    private sealed record RasGateApiResponse<T>
    {
        public required bool Success { get; init; }

        public T? Data { get; init; }

        public RasGateApiError? Error { get; init; }
    }

    private sealed record RasGateApiError
    {
        public required string Code { get; init; }
    }

    private sealed record RasGateStatusData
    {
        public required string InstanceName { get; init; }

        public required string Version { get; init; }
    }

    private sealed record RacStatusData
    {
        public required bool Available { get; init; }

        public required string? Version { get; init; }
    }

    private readonly record struct RacMutation(
        string Resource,
        string Operation);

    private sealed record ExecuteRacData
    {
        public string? Outcome { get; init; }

        public required int? ExitCode { get; init; }

        public required string? StandardOutput { get; init; }

        public required string? StandardError { get; init; }

        public required long? DurationMilliseconds { get; init; }

        public required bool? TimedOut { get; init; }
    }
}