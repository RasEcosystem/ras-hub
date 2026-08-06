using RasHub.Contracts.Common.Pagination;

namespace RasHub.Contracts.UnitTests.Pagination;

public sealed class PageResultTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(21, 10, 3)]
    [InlineData(10, 0, 0)]
    [InlineData(10, -1, 0)]
    public void TotalPages_calculates_the_number_of_pages(
        int totalCount,
        int pageSize,
        int expected)
    {
        var result = new PageResult<object>
        {
            TotalCount = totalCount,
            PageSize = pageSize
        };

        Assert.Equal(expected, result.TotalPages);
    }
}