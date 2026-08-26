using RasHub.Infrastructure.Extensions;

namespace RasHub.Infrastructure.UnitTests.Extensions;

public sealed class PaginationExtensionsTests
{
    [Theory]
    [InlineData(1, 3, new[] { 1, 2, 3 })]
    [InlineData(2, 3, new[] { 4, 5, 6 })]
    [InlineData(3, 3, new[] { 7 })]
    [InlineData(4, 3, new int[] { })]
    public void Enumerable_returns_the_requested_page(
        int page,
        int pageSize,
        int[] expected)
    {
        var source = Enumerable.Range(1, 7);

        var result = source.ApplyPagination(page, pageSize);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1, 3, new[] { 1, 2, 3 })]
    [InlineData(2, 3, new[] { 4, 5, 6 })]
    [InlineData(3, 3, new[] { 7 })]
    [InlineData(4, 3, new int[] { })]
    public void Queryable_returns_the_requested_page(
        int page,
        int pageSize,
        int[] expected)
    {
        var source = Enumerable.Range(1, 7).AsQueryable();

        var result = source.ApplyPagination(page, pageSize);

        Assert.Equal(expected, result);
    }
}
