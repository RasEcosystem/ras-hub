namespace RasHub.Web.Authentication;

public static class ApiDocumentationAuthenticationDefaults
{
    public const string Scheme = "ApiDocumentationCookie";
    public const string Policy = "ApiDocumentationAccess";
    public const string LoginPath = "/swagger/login";
    public const string LogoutPath = "/swagger/logout";
}