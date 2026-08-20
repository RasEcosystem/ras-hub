using System.ComponentModel.DataAnnotations;
using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests.Clusters;

public sealed class UpdateClusterRequestTests
{
    [Fact]
    public void Validate_without_setting_rejects_request()
    {
        Assert.Single(Validate(new UpdateClusterRequest()));
    }

    [Fact]
    public void Validate_password_without_user_rejects_request()
    {
        var results = Validate(new UpdateClusterRequest(
            "Updated",
            AgentPassword: "agent-secret"));

        Assert.Contains(results,
            result => result.MemberNames.Contains(
                nameof(UpdateClusterRequest.AgentUser)));
    }

    [Fact]
    public void ToString_with_credentials_does_not_expose_values()
    {
        var request = new UpdateClusterRequest(
            "Updated",
            AgentUser: "agent-admin",
            AgentPassword: "agent-secret");

        Assert.Equal(nameof(UpdateClusterRequest), request.ToString());
    }

    private static IReadOnlyList<ValidationResult> Validate(
        UpdateClusterRequest request)
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