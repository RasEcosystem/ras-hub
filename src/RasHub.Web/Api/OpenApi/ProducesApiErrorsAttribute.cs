namespace RasHub.Web.Api.OpenApi;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = true)]
public sealed class ProducesApiErrorsAttribute : Attribute
{
    public ProducesApiErrorsAttribute(params int[] statusCodes)
    {
        ArgumentNullException.ThrowIfNull(statusCodes);

        if (statusCodes.Length == 0)
            throw new ArgumentException(
                "At least one API error status code is required.",
                nameof(statusCodes));

        foreach (var statusCode in statusCodes)
            if (statusCode is < 400 or > 599)
                throw new ArgumentOutOfRangeException(
                    nameof(statusCodes),
                    statusCode,
                    "API error status codes must be between 400 and 599.");

        StatusCodes = statusCodes.Distinct().ToArray();
    }

    public IReadOnlyList<int> StatusCodes { get; }
}
