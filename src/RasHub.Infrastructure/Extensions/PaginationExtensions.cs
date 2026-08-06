namespace RasHub.Infrastructure.Extensions;

public static class PaginationExtensions
{
    public static IQueryable<T> ApplyPagination<T>(
        this IQueryable<T> query,
        int page,
        int pageSize
    )
    {
        return query
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }

    public static IEnumerable<T> ApplyPagination<T>(
        this IEnumerable<T> source,
        int page,
        int pageSize
    )
    {
        return source
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}