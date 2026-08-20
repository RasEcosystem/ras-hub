using System.Text.Json;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;

namespace RasHub.Contracts.UnitTests.RasHub.Requests;

public sealed class ContractRequestSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void CreateClusterRequest_json_round_trip_preserves_value()
    {
        var request = new CreateClusterRequest(
            "cluster.example.test",
            1541,
            "Production cluster",
            LoadBalancingMode: ClusterLoadBalancingMode.Performance,
            AgentUser: "agent-admin",
            AgentPassword: "agent-secret");

        var json = JsonSerializer.Serialize(request, SerializerOptions);
        var result = JsonSerializer.Deserialize<CreateClusterRequest>(
            json,
            SerializerOptions);

        Assert.Equal(request, result);
    }

    [Fact]
    public void SynchronizeInfobasesRequest_json_round_trip_preserves_value()
    {
        var request = new SynchronizeInfobasesRequest
        {
            Page = 3,
            PageSize = 25,
            ClusterUser = "cluster-admin",
            ClusterPassword = "cluster-secret"
        };

        var json = JsonSerializer.Serialize(request, SerializerOptions);
        var result = JsonSerializer.Deserialize<SynchronizeInfobasesRequest>(
            json,
            SerializerOptions);

        Assert.NotNull(result);
        Assert.Equal(request.Page, result.Page);
        Assert.Equal(request.PageSize, result.PageSize);
        Assert.Equal(request.ClusterUser, result.ClusterUser);
        Assert.Equal(request.ClusterPassword, result.ClusterPassword);
    }
}