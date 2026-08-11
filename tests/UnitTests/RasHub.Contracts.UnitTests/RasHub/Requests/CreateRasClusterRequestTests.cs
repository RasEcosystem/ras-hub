using System.ComponentModel.DataAnnotations;
using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests;

public sealed class CreateRasClusterRequestTests
{
    [Fact]
    public void Validate_required_settings_accepts_request()
    {
        Assert.Empty(Validate(new CreateRasClusterRequest("localhost", 1587)));
    }

    [Fact]
    public void Validate_password_without_user_rejects_request()
    {
        var results = Validate(new CreateRasClusterRequest(
            "localhost",
            1587,
            AgentPassword: "agent-secret"));

        Assert.Contains(results, result => result.MemberNames.Contains(
            nameof(CreateRasClusterRequest.AgentUser)));
    }

    private static IReadOnlyList<ValidationResult> Validate(
        CreateRasClusterRequest request)
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