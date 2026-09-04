using System.Net;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Web.Api.Filters;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasEndpoints;
using RasHub.Web.Api.RasGates;

namespace RasHub.Web.Api;

internal static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddRasHubApi(this IServiceCollection services)
    {
        services.AddScoped<ActiveRasGateLookup>();
        services.AddScoped<ActiveRasEndpointLookup>();
        services.AddScoped<InteractiveTaskRunner>();

        services.ConfigureHttpJsonOptions(options =>
            ApiJson.Configure(options.SerializerOptions));

        services.AddRouting(options =>
        {
            options.LowercaseUrls = true;
            options.LowercaseQueryStrings = true;
        });

        services.Configure<MvcOptions>(options =>
            options.Filters.Add(new ProducesAttribute("application/json")));

        services
            .AddControllers(options =>
                options.Filters.Add<ApiResponseResultFilter>())
            .AddJsonOptions(options =>
                ApiJson.Configure(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .SelectMany(entry => entry.Value!.Errors.Select(error =>
                            new ApiError(
                                "validation_error",
                                error.ErrorMessage,
                                entry.Key)))
                        .ToList();

                    return new BadRequestObjectResult(
                        ApiResponse<object>.Fail(HttpStatusCode.BadRequest, errors));
                };
            });

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<ApiKeySecurityTransformer>();
            options.AddDocumentTransformer<ControllerDescriptionTransformer>();
            options.AddOperationTransformer<ApiKeySecurityTransformer>();
            options.AddOperationTransformer<ApiErrorResponseTransformer>();
        });

        return services;
    }
}
