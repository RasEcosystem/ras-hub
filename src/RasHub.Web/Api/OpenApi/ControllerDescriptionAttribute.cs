namespace RasHub.Web.Api.OpenApi;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ControllerDescriptionAttribute : Attribute
{
    public ControllerDescriptionAttribute(string tag, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Tag = tag;
        Description = description;
    }

    public string Tag { get; }

    public string Description { get; }
}
