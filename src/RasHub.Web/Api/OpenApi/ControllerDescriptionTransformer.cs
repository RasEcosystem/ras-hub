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
                Attribute = action.ControllerTypeInfo
                    .GetCustomAttribute<ControllerDescriptionAttribute>(true)
            })
            .Where(item => item.Attribute is not null)
            .Select(item => item.Attribute!)
            .DistinctBy(attribute => attribute.Tag);

        document.Tags ??= new HashSet<OpenApiTag>();

        foreach (var attribute in controllerDescriptions)
        {
            var existingTag = document.Tags.FirstOrDefault(tag => string.Equals(
                tag.Name,
                attribute.Tag,
                StringComparison.Ordinal));

            if (existingTag is not null)
            {
                existingTag.Description = attribute.Description;
                continue;
            }

            document.Tags.Add(new OpenApiTag { Name = attribute.Tag, Description = attribute.Description });
        }

        return Task.CompletedTask;
    }
}