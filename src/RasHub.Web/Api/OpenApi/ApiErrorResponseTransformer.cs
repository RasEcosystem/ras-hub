using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;
using RasHub.Web.Infrastructure.Authorization;

namespace RasHub.Web.Api.OpenApi;

public sealed class ApiErrorResponseTransformer : IOpenApiOperationTransformer
{
    private const string ErrorSchemaId = nameof(OpenApiErrorResponse);

    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var statusCodes = metadata
            .OfType<ProducesApiErrorsAttribute>()
            .SelectMany(attribute => attribute.StatusCodes)
            .ToHashSet();

        var authorizeData = metadata.OfType<IAuthorizeData>().ToArray();
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();

        if (!allowsAnonymous && authorizeData.Length > 0)
        {
            statusCodes.Add(StatusCodes.Status401Unauthorized);

            if (authorizeData.Any(data =>
                    string.Equals(
                        data.Policy,
                        AppPolicies.ManageRasGates,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        data.Policy,
                        AppPolicies.ManageRasEndpoints,
                        StringComparison.Ordinal)))
                statusCodes.Add(StatusCodes.Status403Forbidden);
        }

        if (statusCodes.Count == 0)
            return;

        operation.Responses ??= new OpenApiResponses();

        statusCodes.RemoveWhere(statusCode => operation.Responses.ContainsKey(
            statusCode.ToString(CultureInfo.InvariantCulture)));

        if (statusCodes.Count == 0)
            return;

        var document = context.Document ?? throw new InvalidOperationException(
            "The OpenAPI document is unavailable during transformation.");

        var errorSchema = await context.GetOrCreateSchemaAsync(
            typeof(OpenApiErrorResponse),
            null,
            cancellationToken);

        document.AddComponent(ErrorSchemaId, errorSchema);

        foreach (var statusCode in statusCodes.Order())
            operation.Responses.TryAdd(
                statusCode.ToString(CultureInfo.InvariantCulture),
                new OpenApiResponse
                {
                    Description = GetDescription(statusCode),
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new()
                        {
                            Schema = new OpenApiSchemaReference(
                                ErrorSchemaId,
                                document)
                        }
                    }
                });
    }

    private static string GetDescription(int statusCode)
    {
        var reasonPhrase = ReasonPhrases.GetReasonPhrase(statusCode);

        return string.IsNullOrWhiteSpace(reasonPhrase)
            ? $"HTTP {statusCode} error"
            : reasonPhrase;
    }
}
