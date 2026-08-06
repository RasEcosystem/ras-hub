using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RasHub.Web.Api.OpenApi;

public sealed class ControllerDescriptionTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var controllerDescriptions = context.DescriptionGroups
            .SelectMany(group => group.Items)
            .Select(description => description.ActionDescriptor)
            .OfType<ControllerActionDescriptor>()
            .Select(action => new
            {
                Tag = action.ControllerName,
                action.ControllerTypeInfo
                    .GetCustomAttribute<ControllerDescriptionAttribute>(true)
                    ?.Description
            })
            .Where(item => item.Description is not null)
            .DistinctBy(item => item.Tag);

        document.Tags ??= new HashSet<OpenApiTag>();

        foreach (var controller in controllerDescriptions)
        {
            var existingTag = document.Tags.FirstOrDefault(tag => string.Equals(
                tag.Name,
                controller.Tag,
                StringComparison.Ordinal));

            if (existingTag is not null)
            {
                existingTag.Description = controller.Description;
                continue;
            }

            document.Tags.Add(new OpenApiTag
            {
                Name = controller.Tag,
                Description = controller.Description
            });
        }

        return Task.CompletedTask;
    }
}