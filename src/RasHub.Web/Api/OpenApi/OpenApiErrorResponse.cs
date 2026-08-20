using System.ComponentModel;
using System.Text.Json.Serialization;
using RasHub.Contracts.Common;

namespace RasHub.Web.Api.OpenApi;

public sealed class OpenApiErrorResponse
{
    [JsonPropertyOrder(0)]
    [DefaultValue(false)]
    public bool Success { get; }

    [JsonPropertyOrder(1)] public ApiError? Error { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyOrder(2)]
    public IReadOnlyCollection<ApiError>? Errors { get; init; }
}
