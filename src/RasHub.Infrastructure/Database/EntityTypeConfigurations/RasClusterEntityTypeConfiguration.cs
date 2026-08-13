using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database.EntityTypeConfigurations;

public sealed class RasClusterEntityTypeConfiguration
    : IEntityTypeConfiguration<RasCluster>
{
    public void Configure(EntityTypeBuilder<RasCluster> builder)
    {
        builder.ToTable("ras_clusters", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_ras_clusters_port",
                "port BETWEEN 1 AND 65535");
        });

        builder.ConfigureCommonFields();

        builder.Property(cluster => cluster.RasGateId)
            .HasColumnName("ras_gate_id")
            .IsRequired();

        builder.Property(cluster => cluster.ExternalId)
            .HasColumnName("external_id")
            .IsRequired();

        builder.Property(cluster => cluster.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(cluster => cluster.Host)
            .HasColumnName("host")
            .IsRequired();

        builder.Property(cluster => cluster.Port)
            .HasColumnName("port")
            .IsRequired();

        builder.Property(cluster => cluster.ExpirationTimeoutSeconds)
            .HasColumnName("expiration_timeout_seconds")
            .IsRequired();

        builder.Property(cluster => cluster.LifetimeLimitSeconds)
            .HasColumnName("lifetime_limit_seconds")
            .IsRequired();

        builder.Property(cluster => cluster.MaxMemorySizeKb)
            .HasColumnName("max_memory_size_kb")
            .IsRequired();

        builder.Property(cluster => cluster.MaxMemoryTimeLimitSeconds)
            .HasColumnName("max_memory_time_limit_seconds")
            .IsRequired();

        builder.Property(cluster => cluster.SecurityLevel)
            .HasColumnName("security_level")
            .IsRequired();

        builder.Property(cluster => cluster.SessionFaultToleranceLevel)
            .HasColumnName("session_fault_tolerance_level")
            .IsRequired();

        builder.Property(cluster => cluster.LoadBalancingMode)
            .HasColumnName("load_balancing_mode")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(cluster => cluster.ErrorsCountThresholdPercent)
            .HasColumnName("errors_count_threshold_percent")
            .IsRequired();

        builder.Property(cluster => cluster.KillProblemProcesses)
            .HasColumnName("kill_problem_processes")
            .IsRequired();

        builder.Property(cluster => cluster.KillByMemoryWithDump)
            .HasColumnName("kill_by_memory_with_dump");

        builder.Property(cluster => cluster.AllowAccessRightAuditEventsRecording)
            .HasColumnName("allow_access_right_audit_events_recording");

        builder.Property(cluster => cluster.PingPeriod)
            .HasColumnName("ping_period");

        builder.Property(cluster => cluster.PingTimeout)
            .HasColumnName("ping_timeout");

        builder.Property(cluster => cluster.RestartSchedule)
            .HasColumnName("restart_schedule");

        builder.Property(cluster => cluster.ObservedAt)
            .HasColumnName("observed_at")
            .IsRequired();

        builder.HasIndex(cluster => new
        {
            cluster.RasGateId,
            cluster.ExternalId
        })
            .IsUnique()
            .HasDatabaseName("ux_ras_clusters_ras_gate_id_external_id");

        builder.HasOne<RasGate>()
            .WithMany()
            .HasForeignKey(cluster => cluster.RasGateId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ras_clusters_ras_gates_ras_gate_id");
    }
}