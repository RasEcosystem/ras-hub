using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace RasHub.Web.Infrastructure.Security;

public static class DataProtectionServiceCollectionExtensions
{
    private const string SectionName = "DataProtection";
    private const string DefaultApplicationName = "RasHub";

    public static IDataProtectionBuilder AddRasHubDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var applicationName = section["ApplicationName"];
        var keysPath = section["KeysPath"];
        var certificatePath = section["CertificatePath"];
        var certificatePassword = section["CertificatePassword"];
        var certificatePasswordFile = section["CertificatePasswordFile"];

        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName(
                string.IsNullOrWhiteSpace(applicationName)
                    ? DefaultApplicationName
                    : applicationName);

        if (!string.IsNullOrWhiteSpace(keysPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            if (!string.IsNullOrWhiteSpace(certificatePassword) ||
                !string.IsNullOrWhiteSpace(certificatePasswordFile))
                throw new InvalidOperationException(
                    $"{SectionName}:CertificatePath is required when a certificate password is configured.");

            return dataProtection;
        }

        if (!string.IsNullOrWhiteSpace(certificatePassword) &&
            !string.IsNullOrWhiteSpace(certificatePasswordFile))
            throw new InvalidOperationException(
                $"Configure either {SectionName}:CertificatePassword or " +
                $"{SectionName}:CertificatePasswordFile, not both.");

        if (!string.IsNullOrWhiteSpace(certificatePasswordFile))
            certificatePassword = File
                .ReadAllText(certificatePasswordFile)
                .TrimEnd('\r', '\n');

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);

        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException(
                $"Data Protection certificate '{certificatePath}' does not contain a private key.");

        return dataProtection.ProtectKeysWithCertificate(certificate);
    }
}
