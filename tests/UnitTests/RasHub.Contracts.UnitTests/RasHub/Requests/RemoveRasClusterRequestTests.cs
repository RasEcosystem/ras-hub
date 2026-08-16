using System.ComponentModel.DataAnnotations;
using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests;

public sealed class RemoveRasClusterRequestTests
{
    [Fact]
    public void Validate_password_with_user_accepts_request()
    {
        var request = new RemoveRasClusterRequest(
            "cluster-admin",
            "cluster-secret");

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_without_credentials_accepts_request()
    {
        Assert.Empty(Validate(new RemoveRasClusterRequest()));
    }

    [Fact]
    public void Validate_password_without_user_rejects_request()
    {
        var results = Validate(new RemoveRasClusterRequest(
            ClusterPassword: "cluster-secret"));

        var result = Assert.Single(results);
        Assert.Contains(nameof(RemoveRasClusterRequest.ClusterUser),
            result.MemberNames);
    }

    private static IReadOnlyList<ValidationResult> Validate(
        RemoveRasClusterRequest request)
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