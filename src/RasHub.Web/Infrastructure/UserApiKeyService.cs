using Microsoft.EntityFrameworkCore;
using RasHub.Web.Data;

namespace RasHub.Web.Infrastructure;

public sealed class UserApiKeyService(
    CurrentUserAccessor currentUserAccessor,
    ApplicationDbContext dbContext)
{
    public async Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUserAccessor.GetUserIdAsync();

        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.ApiKey)
            .SingleAsync(cancellationToken);
    }
}