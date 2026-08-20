namespace RasHub.Web.Infrastructure.Logging;

public static class SwaggerUiUrlResolver
{
    public static string Resolve(Uri applicationBaseUri)
    {
        ArgumentNullException.ThrowIfNull(applicationBaseUri);

        if (!applicationBaseUri.IsAbsoluteUri)
            throw new ArgumentException(
                "The application base URI must be absolute.",
                nameof(applicationBaseUri));

        var builder = new UriBuilder(applicationBaseUri)
        {
            Path = $"{applicationBaseUri.AbsolutePath.TrimEnd('/')}/swagger/",
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri;
    }
}
