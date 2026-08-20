using System.ComponentModel.DataAnnotations;
using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests.Infobases;

public sealed class SynchronizeInfobaseRequestTests
{
    [Fact]
    public void Validate_password_with_user_accepts_requests()
    {
        var listRequest = new SynchronizeInfobasesRequest
        {
            ClusterUser = "cluster-admin",
            ClusterPassword = "cluster-secret"
        };
        var itemRequest = new SynchronizeInfobaseRequest
        {
            ClusterUser = "cluster-admin",
            ClusterPassword = "cluster-secret"
        };

        Assert.Empty(Validate(listRequest));
        Assert.Empty(Validate(itemRequest));
    }

    [Fact]
    public void Validate_password_without_user_rejects_requests()
    {
        var listResults = Validate(new SynchronizeInfobasesRequest
        {
            ClusterPassword = "cluster-secret"
        });
        var itemResults = Validate(new SynchronizeInfobaseRequest
        {
            ClusterPassword = "cluster-secret"
        });

        Assert.Contains(nameof(SynchronizeInfobasesRequest.ClusterUser),
            Assert.Single(listResults).MemberNames);
        Assert.Contains(nameof(SynchronizeInfobaseRequest.ClusterUser),
            Assert.Single(itemResults).MemberNames);
    }

    [Fact]
    public void ToString_does_not_expose_credentials()
    {
        var listRequest = new SynchronizeInfobasesRequest
        {
            ClusterUser = "cluster-admin",
            ClusterPassword = "cluster-secret"
        };
        var itemRequest = new SynchronizeInfobaseRequest
        {
            ClusterUser = "cluster-admin",
            ClusterPassword = "cluster-secret"
        };

        Assert.Equal(nameof(SynchronizeInfobasesRequest),
            listRequest.ToString());
        Assert.Equal(nameof(SynchronizeInfobaseRequest),
            itemRequest.ToString());
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