using RasHub.Infrastructure.RasGates.Rac.Clusters.Commands;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Clusters.Commands;

public sealed class RemoveRasClusterCommandTests
{
    [Fact]
    public void ToString_command_contains_password_does_not_disclose_secret()
    {
        const string password = "cluster-secret";
        var command = new RemoveRasClusterCommand(
            Guid.NewGuid(),
            "administrator",
            password);

        Assert.DoesNotContain(password, command.ToString());
    }
}
