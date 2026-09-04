using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using RasHub.Web.Data;

namespace RasHub.Web.Components.Account;

internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Identity.StatusMessage";

    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
        SameSite = SameSiteMode.Strict, HttpOnly = true, IsEssential = true, MaxAge = TimeSpan.FromSeconds(5)
    };

    private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    public void RedirectTo(string? uri)
    {
        navigationManager.NavigateTo(GetSafeRedirectUri(uri));
    }

    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
        RedirectTo(uri);
    }

    public void RedirectToCurrentPage()
    {
        RedirectTo(CurrentPath);
    }

    public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
    {
        RedirectToWithStatus(CurrentPath, message, context);
    }

    public void RedirectToInvalidUser(UserManager<ApplicationUser> userManager, HttpContext context)
    {
        RedirectToWithStatus("Account/InvalidUser",
            $"Error: Unable to load user with ID '{userManager.GetUserId(context.User)}'.",
            context);
    }

    private string GetSafeRedirectUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
            return "";

        if (uri.Contains('\\') ||
            uri.Any(char.IsControl))
            return "";

        var baseUri = new Uri(navigationManager.BaseUri);

        if (!Uri.TryCreate(baseUri, uri, out var targetUri) ||
            !IsSameOrigin(baseUri, targetUri) ||
            !baseUri.IsBaseOf(targetUri) ||
            !string.IsNullOrEmpty(targetUri.UserInfo))
            return "";

        return targetUri.AbsoluteUri;
    }

    private static bool IsSameOrigin(Uri first, Uri second)
    {
        return string.Equals(first.Scheme, second.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(first.IdnHost, second.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               first.Port == second.Port;
    }
}
