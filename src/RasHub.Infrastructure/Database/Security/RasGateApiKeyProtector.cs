using Microsoft.AspNetCore.DataProtection;

namespace RasHub.Infrastructure.Database.Security;

public sealed class RasGateApiKeyProtector
{
    private const string Purpose = "RasHub.RasGate.ApiKey.v1";
    private const string Prefix = "rashub-dp:v1:";

    private readonly IDataProtector _protector;

    public RasGateApiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public bool IsProtected(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public string Protect(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return IsProtected(value)
            ? value
            : Prefix + _protector.Protect(value);
    }

    public string Unprotect(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return IsProtected(value)
            ? _protector.Unprotect(value[Prefix.Length..])
            : value;
    }
}