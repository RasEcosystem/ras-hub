using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RasHub.Web.Infrastructure.Security;

namespace RasHub.Web.IntegrationTests.Infrastructure;

public sealed class DataProtectionConfigurationTests
{
    [Fact]
    public void Persisted_encrypted_key_ring_can_be_reused()
    {
        var root = Directory.CreateTempSubdirectory("rashub-data-protection-");

        try
        {
            const string certificatePassword = "test-certificate-password";
            var keysPath = Directory.CreateDirectory(
                Path.Combine(root.FullName, "keys"));
            var certificatePath = Path.Combine(root.FullName, "data-protection.pfx");
            var passwordPath = Path.Combine(root.FullName, "certificate-password");

            CreateCertificate(certificatePath, certificatePassword);
            File.WriteAllText(passwordPath, $"{certificatePassword}\n");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataProtection:ApplicationName"] = "RasHub.Tests",
                    ["DataProtection:KeysPath"] = keysPath.FullName,
                    ["DataProtection:CertificatePath"] = certificatePath,
                    ["DataProtection:CertificatePasswordFile"] = passwordPath
                })
                .Build();

            string protectedValue;

            using (var services = CreateServices(configuration))
            {
                protectedValue = services
                    .GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("persistent-key-ring-test")
                    .Protect("retained-value");
            }

            using (var services = CreateServices(configuration))
            {
                var value = services
                    .GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("persistent-key-ring-test")
                    .Unprotect(protectedValue);

                Assert.Equal("retained-value", value);
            }

            var keyDocument = File.ReadAllText(
                Directory.EnumerateFiles(keysPath.FullName, "*.xml").Single());
            Assert.Contains("EncryptedData", keyDocument);
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static ServiceProvider CreateServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRasHubDataProtection(configuration);
        return services.BuildServiceProvider();
    }

    private static void CreateCertificate(string path, string password)
    {
        using var key = RSA.Create(2_048);
        var request = new CertificateRequest(
            "CN=RasHub Data Protection Test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        File.WriteAllBytes(
            path,
            certificate.Export(X509ContentType.Pfx, password));
    }
}
