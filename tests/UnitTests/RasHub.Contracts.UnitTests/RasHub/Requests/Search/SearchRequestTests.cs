using System.ComponentModel.DataAnnotations;
using RasHub.Contracts.RasHub.Requests.Search;

namespace RasHub.Contracts.UnitTests.RasHub.Requests.Search;

public sealed class SearchRequestTests
{
    [Fact]
    public void Validate_whitespace_query_rejects_request()
    {
        var results = Validate(new SearchRasGatesRequest { Query = "  " });

        Assert.Contains(
            nameof(SearchRasGatesRequest.Query),
            Assert.Single(results).MemberNames);
    }

    [Fact]
    public void Validate_empty_endpoint_filter_rejects_request()
    {
        var results = Validate(new SearchClustersRequest { Query = "cluster", RasEndpointId = Guid.Empty });

        Assert.Contains(
            nameof(SearchClustersRequest.RasEndpointId),
            Assert.Single(results).MemberNames);
    }

    [Fact]
    public void Validate_cluster_filter_without_endpoint_filter_rejects_request()
    {
        var results = Validate(new SearchInfobasesRequest { Query = "infobase", ClusterId = Guid.NewGuid() });

        Assert.Contains(
            nameof(SearchInfobasesRequest.RasEndpointId),
            Assert.Single(results).MemberNames);
    }

    [Fact]
    public void Validate_undefined_search_field_rejects_request()
    {
        var results = Validate(new SearchInfobasesRequest { Query = "infobase", Fields = [(InfobaseSearchField)999] });

        Assert.Contains(
            nameof(SearchInfobasesRequest.Fields),
            Assert.Single(results).MemberNames);
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
