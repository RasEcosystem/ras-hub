using System.ComponentModel.DataAnnotations;
using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests.Clusters;

public sealed class RemoveClusterRequestTests
{
    [Fact]
    public void Validate_password_with_user_accepts_request()
    {
        var request = new RemoveClusterRequest(
            "cluster-admin",
            "cluster-secret");

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_without_credentials_accepts_request()
    {
        Assert.Empty(Validate(new RemoveClusterRequest()));
    }

    [Fact]
    public void Validate_password_without_user_rejects_request()
    {
        var results = Validate(new RemoveClusterRequest(
            ClusterPassword: "cluster-secret"));

        var result = Assert.Single(results);
        Assert.Contains(nameof(RemoveClusterRequest.ClusterUser),
            result.MemberNames);
    }

    [Fact]
    public void ToString_with_credentials_does_not_expose_values()
    {
        var request = new RemoveClusterRequest(
            "cluster-admin",
            "cluster-secret");

        Assert.Equal(nameof(RemoveClusterRequest), request.ToString());
    }

    private static IReadOnlyList<ValidationResult> Validate(
        RemoveClusterRequest request)
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