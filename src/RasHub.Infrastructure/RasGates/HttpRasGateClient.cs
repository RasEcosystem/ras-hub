using System.Net.Http.Json;
using System.Text.Json;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Serialization;

namespace RasHub.Infrastructure.RasGates;

public sealed class HttpRasGateClient : IRasGateClient
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _apiKey;
    private readonly Uri _baseAddress;

    private readonly IRacOutputDeserializer<IReadOnlyList<RasClusterSnapshot>>
        _clusterDeserializer;

    private readonly HttpClient _httpClient;

    public HttpRasGateClient(
        HttpClient httpClient,
        Uri baseAddress,
        string apiKey,
        IRacOutputDeserializer<IReadOnlyList<RasClusterSnapshot>> clusterDeserializer)
    {
        _httpClient = httpClient;
        _baseAddress = baseAddress;
        _apiKey = apiKey;
        _clusterDeserializer = clusterDeserializer;
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

    public async Task<IReadOnlyList<RasClusterSnapshot>> GetClustersAsync(
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "rac/execute");
        request.Content = JsonContent.Create(
            new ExecuteRacRequest
            {
                Arguments = ["cluster", "list"]
            },
            options: JsonOptions);

        var data = await SendAsync<ExecuteRacData>(request, cancellationToken);

        if (data.ExitCode is null ||
            data.TimedOut is null ||
            data.StandardOutput is null)
            throw new RasGateClientException(
                "RasGate returned an incomplete RAC execution response.");

        if (data.TimedOut.Value)
            throw new RasGateClientException("RAC cluster list command timed out.");

        if (data.ExitCode.Value != 0)
            throw new RasGateClientException(
                $"RAC cluster list command failed with exit code {data.ExitCode.Value}.");

        return _clusterDeserializer.Deserialize(data.StandardOutput);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUri)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(_baseAddress, relativeUri));
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
        public string Code { get; } = string.Empty;
    }

    private sealed record RasGateStatusData
    {
        public string InstanceName { get; } = string.Empty;

        public string Version { get; } = string.Empty;
    }

    private sealed record ExecuteRacData
    {
        public int? ExitCode { get; init; }

        public string? StandardOutput { get; init; }

        public bool? TimedOut { get; init; }
    }
}