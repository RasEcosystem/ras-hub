namespace RasHub.Web.Api.OpenApi;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ControllerDescriptionAttribute : Attribute
{
    public ControllerDescriptionAttribute(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description;
    }

    public string Description { get; }
}