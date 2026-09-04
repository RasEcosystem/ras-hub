using Microsoft.EntityFrameworkCore;
using RasHub.Infrastructure.Database;

namespace RasHub.Web.Infrastructure;

public sealed class RasHubAdministrationMutationRunner(
    RasHubDbContext dbContext)
{
    public async Task<T> RunAsync<T>(Func<Task<T>> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        try
        {
            return await mutation();
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }
}
