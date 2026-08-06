namespace RasHub.Web.Authentication;

public sealed class ApiDocumentationOptions
{
    public const string SectionName = "ApiDocumentation";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}