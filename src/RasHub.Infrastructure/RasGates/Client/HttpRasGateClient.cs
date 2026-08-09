using System.Net.Http.Json;
using System.Text.Json;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;

namespace RasHub.Infrastructure.RasGates.Client;

public sealed class HttpRasGateClient : IRasGateClient
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _apiKey;
    private readonly Uri _baseAddress;
    private readonly RacCapabilityResolver _capabilityResolver;

    private readonly RacResourceAdapterResolver<RasClusterSnapshot>
        _clusterAdapterResolver;

    private readonly HttpClient _httpClient;
    private readonly RacVersionParser _versionParser;
    private Version? _racVersion;

    public HttpRasGateClient(
        HttpClient httpClient,
        Uri baseAddress,
        string apiKey,
        RacVersionParser versionParser,
        RacCapabilityResolver capabilityResolver,
        RacResourceAdapterResolver<RasClusterSnapshot> clusterAdapterResolver)
    {
        _httpClient = httpClient;
        _baseAddress = baseAddress;
        _apiKey = apiKey;
        _versionParser = versionParser;
        _capabilityResolver = capabilityResolver;
        _clusterAdapterResolver = clusterAdapterResolver;
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

    public async Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
        CancellationToken cancellationToken)
    {
        var racVersion = await GetRacVersionAsync(cancellationToken);
        var adapter = _clusterAdapterResolver.Resolve(
            "clusters",
            "snapshot",
            racVersion);
        var execution = await ExecuteRacAsync(
            adapter.CreateCommand(),
            cancellationToken);

        return adapter.Parse(racVersion, execution);
    }

    public async Task<RasClusterSnapshot> GetClusterAsync(
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        var racVersion = await GetRacVersionAsync(cancellationToken);
        var adapter = _clusterAdapterResolver.Resolve(
            "clusters",
            "info",
            racVersion);
        var execution = await ExecuteRacAsync(
            adapter.CreateCommand(clusterId),
            cancellationToken);
        var snapshot = adapter.Parse(
            racVersion,
            execution,
            clusterId);

        if (snapshot.Completeness != SnapshotCompleteness.Complete ||
            snapshot.Items.Count != 1)
            throw new RasGateClientException(
                "RasGate returned an incomplete cluster result.");

        return snapshot.Items[0];
    }

    private async Task<Version> GetRacVersionAsync(
        CancellationToken cancellationToken)
    {
        if (_racVersion is not null)
            return _racVersion;

        using var request = CreateRequest(HttpMethod.Get, "rac/status");
        var data = await SendAsync<RacStatusData>(request, cancellationToken);

        if (!data.Available || string.IsNullOrWhiteSpace(data.Version))
            throw new RasGateClientException("RAC is unavailable through RasGate.");

        _racVersion = _versionParser.Parse(data.Version);
        return _racVersion;
    }

    private async Task<RacExecutionResult> ExecuteRacAsync(
        IReadOnlyList<string> arguments,
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

        var data = await SendAsync<ExecuteRacData>(request, cancellationToken);

        if (data.ExitCode is null ||
            data.TimedOut is null ||
            data.StandardOutput is null ||
            data.StandardError is null ||
            data.DurationMilliseconds is null)
            throw new RasGateClientException(
                "RasGate returned an incomplete RAC execution response.");

        return new RacExecutionResult
        {
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
        CancellationToken cancellationToken)
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
                throw new RasGateClientException(
                    $"RasGate returned HTTP status code {(int)response.StatusCode}.");

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
                throw new RasGateClientException("RasGate returned an empty response.");

            if (!envelope.Success)
                throw new RasGateClientException(
                    envelope.Error is null
                        ? "RasGate reported a request failure."
                        : $"RasGate reported error '{envelope.Error.Code}'.");

            return envelope.Data ?? throw new RasGateClientException(
                "RasGate returned an incomplete response.");
        }
    }

    private sealed record ExecuteRacRequest
    {
        public required IReadOnlyList<string> Arguments { get; init; }
    }

    private sealed record RasGateApiResponse<T>
    {
        public bool Success { get; init; }

        public T? Data { get; init; }

        public RasGateApiError? Error { get; init; }
    }

    private sealed record RasGateApiError
    {
        // Keep init: System.Text.Json must populate this private transport DTO.
        public string Code { get; init; } = string.Empty;
    }

    private sealed record RasGateStatusData
    {
        // Keep init accessors: System.Text.Json must populate this private transport DTO.
        public string InstanceName { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;
    }

    private sealed record RacStatusData
    {
        public bool Available { get; init; }

        public string? Version { get; init; }
    }

    private sealed record ExecuteRacData
    {
        public int? ExitCode { get; init; }

        public string? StandardOutput { get; init; }

        public string? StandardError { get; init; }

        public long? DurationMilliseconds { get; init; }

        public bool? TimedOut { get; init; }
    }
}
