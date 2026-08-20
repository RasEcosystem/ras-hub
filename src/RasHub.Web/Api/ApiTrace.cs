using System.Diagnostics;

namespace RasHub.Web.Api;

public static class ApiTrace
{
    public const string HeaderName = "X-Trace-Id";

    public static string GetTraceId(HttpContext context)
    {
        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }
}
