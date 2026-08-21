using System.ComponentModel.DataAnnotations;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Contracts.RasHub.Requests.Infobases;

namespace RasHub.Contracts.UnitTests.RasHub.Requests.Infobases;

public sealed class InfobaseCredentialsRequestTests
{
    [Fact]
    public void Validate_password_without_user_rejects_request()
    {
        var results = Validate(new InfobaseCredentialsRequest
        {
            ClusterPassword = "cluster-secret"
        });

        Assert.Contains(
            nameof(InfobaseCredentialsRequest.ClusterUser),
            Assert.Single(results).MemberNames);
    }

    [Fact]
    public void ToString_does_not_expose_credentials()
    {
        var request = new InfobaseCredentialsRequest
        {
            ClusterUser = "cluster-admin",
            ClusterPassword = "cluster-secret"
        };

        Assert.Equal(nameof(InfobaseCredentialsRequest), request.ToString());
    }

    private static IReadOnlyList<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            true);

        return results;
    }
}
