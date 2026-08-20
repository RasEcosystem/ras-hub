using System.ComponentModel.DataAnnotations;
using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests.Clusters;

public sealed class CreateClusterRequestTests
{
    [Fact]
    public void Validate_password_without_user_rejects_request()
    {
        var results = Validate(new CreateClusterRequest(
            "localhost",
            1587,
            AgentPassword: "agent-secret"));

        Assert.Contains(results,
            result => result.MemberNames.Contains(
                nameof(CreateClusterRequest.AgentUser)));
    }

    [Fact]
    public void ToString_with_credentials_does_not_expose_values()
    {
        var request = new CreateClusterRequest(
            "localhost",
            1587,
            AgentUser: "agent-admin",
            AgentPassword: "agent-secret");

        Assert.Equal(nameof(CreateClusterRequest), request.ToString());
    }

    private static IReadOnlyList<ValidationResult> Validate(
        CreateClusterRequest request)
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
