using System.Net.Http.Json;
using System.Text.Json;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

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

    private readonly RacResultCommandAdapterResolver<
        RasClusterCreationOptions,
        Guid> _clusterInsertAdapterResolver;

    private readonly RacCommandAdapterResolver<RemoveRasClusterCommand>
        _clusterRemoveAdapterResolver;

    private readonly RacCommandAdapterResolver<UpdateRasClusterCommand>
        _clusterUpdateAdapterResolver;

    private readonly long _configurationRevision;

    private readonly HttpClient _httpClient;
    private readonly RacVersionCache _racVersionCache;
    private readonly Guid _rasGateId;
    private readonly RacVersionParser _versionParser;
    private Version? _racVersion;

    public HttpRasGateClient(
        HttpClient httpClient,
        Uri baseAddress,
        string apiKey,
        Guid rasGateId,
        long configurationRevision,
        RacVersionCache racVersionCache,
        RacVersionParser versionParser,
        RacCapabilityResolver capabilityResolver,
        RacResourceAdapterResolver<RasClusterSnapshot> clusterAdapterResolver,
        RacResultCommandAdapterResolver<RasClusterCreationOptions, Guid>
            clusterInsertAdapterResolver,
        RacCommandAdapterResolver<UpdateRasClusterCommand>
            clusterUpdateAdapterResolver,
        RacCommandAdapterResolver<RemoveRasClusterCommand>
            clusterRemoveAdapterResolver)
    {
        _httpClient = httpClient;
        _baseAddress = baseAddress;
        _apiKey = apiKey;
        _rasGateId = rasGateId;
        _configurationRevision = configurationRevision;
        _racVersionCache = racVersionCache;
        _versionParser = versionParser;
        _capabilityResolver = capabilityResolver;
        _clusterAdapterResolver = clusterAdapterResolver;
        _clusterInsertAdapterResolver = clusterInsertAdapterResolver;
        _clusterUpdateAdapterResolver = clusterUpdateAdapterResolver;
        _clusterRemoveAdapterResolver = clusterRemoveAdapterResolver;
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

        return ParseRacOutput(() => adapter.Parse(racVersion, execution));
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
        var snapshot = ParseRacOutput(() => adapter.Parse(
            racVersion,
            execution,
            clusterId));

        if (snapshot.Completeness != SnapshotCompleteness.Complete ||
            snapshot.Items.Count != 1)
            throw new RasGateClientException(
                "RasGate returned an incomplete cluster result.");

        return snapshot.Items[0];
    }

    public async Task RemoveClusterAsync(
        Guid clusterId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken)
    {
        var racVersion = await GetRacVersionAsync(cancellationToken);
        var adapter = _clusterRemoveAdapterResolver.Resolve(
            "clusters",
            "remove",
            racVersion);
        var command = new RemoveRasClusterCommand(
            clusterId,
            clusterUser,
            clusterPassword);
        var execution = await ExecuteRacAsync(
            adapter.CreateCommand(command),
            cancellationToken);

        adapter.Validate(racVersion, execution, command);
    }

    public async Task<Guid> CreateClusterAsync(
        RasClusterCreationOptions options,
        CancellationToken cancellationToken)
    {
        var racVersion = await GetRacVersionAsync(cancellationToken);
        var adapter = _clusterInsertAdapterResolver.Resolve(
            "clusters",
            "insert",
            racVersion);
        var execution = await ExecuteRacAsync(
            adapter.CreateCommand(options),
            cancellationToken);

        return ParseRacOutput(() =>
            adapter.Parse(racVersion, execution, options));
    }

    public async Task UpdateClusterAsync(
        Guid clusterId,
        RasClusterUpdateOptions options,
        CancellationToken cancellationToken)
    {
        var racVersion = await GetRacVersionAsync(cancellationToken);
        var adapter = _clusterUpdateAdapterResolver.Resolve(
            "clusters",
            "update",
            racVersion);
        var command = new UpdateRasClusterCommand(clusterId, options);
        var execution = await ExecuteRacAsync(
            adapter.CreateCommand(command),
            cancellationToken);

        adapter.Validate(racVersion, execution, command);
    }

    private async Task<Version> GetRacVersionAsync(
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

    private T ParseRacOutput<T>(Func<T> parse)
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

    private void InvalidateRacVersion()
    {
        _racVersion = null;
        _racVersionCache.Remove(_rasGateId, _configurationRevision);
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

    private sealed record ExecuteRacData
    {
        public required int? ExitCode { get; init; }

        public required string? StandardOutput { get; init; }

        public required string? StandardError { get; init; }

        public required long? DurationMilliseconds { get; init; }

        public required bool? TimedOut { get; init; }
    }
}