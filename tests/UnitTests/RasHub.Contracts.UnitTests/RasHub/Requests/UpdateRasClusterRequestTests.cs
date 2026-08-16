using System.ComponentModel.DataAnnotations;
using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests;

public sealed class UpdateRasClusterRequestTests
{
    [Fact]
    public void Validate_with_setting_accepts_request()
    {
        Assert.Empty(Validate(new UpdateRasClusterRequest("Updated")));
    }

    [Fact]
    public void Validate_without_setting_rejects_request()
    {
        Assert.Single(Validate(new UpdateRasClusterRequest()));
    }

    [Fact]
    public void Validate_password_without_user_rejects_request()
    {
        var results = Validate(new UpdateRasClusterRequest(
            "Updated",
            AgentPassword: "agent-secret"));

        Assert.Contains(results, result => result.MemberNames.Contains(
            nameof(UpdateRasClusterRequest.AgentUser)));
    }

    private static IReadOnlyList<ValidationResult> Validate(
        UpdateRasClusterRequest request)
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