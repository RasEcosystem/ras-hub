using Microsoft.EntityFrameworkCore;
using RasHub.Application.Interfaces;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database;

public sealed class RasHubDbContext(DbContextOptions<RasHubDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public const string ConnectionStringName = "RasHub";

    public DbSet<RasGate> RasGates => Set<RasGate>();

    public DbSet<RasCluster> RasClusters => Set<RasCluster>();

    public DbSet<SettingEntry> Settings => Set<SettingEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RasHubDbContext).Assembly);
    }
}