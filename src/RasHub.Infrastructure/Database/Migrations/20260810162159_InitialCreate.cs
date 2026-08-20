using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RasHub.Infrastructure.Database.Migrations
{
    // The designer intentionally retains the final pre-squash migration ID so
    // databases that completed the former migration chain remain compatible.
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ras_gates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    api_key = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    configuration_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    instance_name = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<string>(type: "text", nullable: true),
                    status_observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ras_gates", x => x.id);
                    table.CheckConstraint("ck_ras_gates_port", "port BETWEEN 1 AND 65535");
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "ras_clusters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ras_gate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    host = table.Column<string>(type: "text", nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    expiration_timeout_seconds = table.Column<long>(type: "bigint", nullable: false),
                    lifetime_limit_seconds = table.Column<long>(type: "bigint", nullable: false),
                    max_memory_size_kb = table.Column<long>(type: "bigint", nullable: false),
                    max_memory_time_limit_seconds = table.Column<long>(type: "bigint", nullable: false),
                    security_level = table.Column<int>(type: "integer", nullable: false),
                    session_fault_tolerance_level = table.Column<int>(type: "integer", nullable: false),
                    load_balancing_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    errors_count_threshold_percent = table.Column<int>(type: "integer", nullable: false),
                    kill_problem_processes = table.Column<bool>(type: "boolean", nullable: false),
                    kill_by_memory_with_dump = table.Column<bool>(type: "boolean", nullable: true),
                    allow_access_right_audit_events_recording = table.Column<bool>(type: "boolean", nullable: true),
                    ping_period = table.Column<long>(type: "bigint", nullable: true),
                    ping_timeout = table.Column<long>(type: "bigint", nullable: true),
                    restart_schedule = table.Column<string>(type: "text", nullable: true),
                    observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ras_clusters", x => x.id);
                    table.CheckConstraint("ck_ras_clusters_port", "port BETWEEN 1 AND 65535");
                    table.ForeignKey(
                        name: "fk_ras_clusters_ras_gates_ras_gate_id",
                        column: x => x.ras_gate_id,
                        principalTable: "ras_gates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_ras_clusters_ras_gate_id_external_id",
                table: "ras_clusters",
                columns: new[] { "ras_gate_id", "external_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ras_clusters");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "ras_gates");
        }
    }
}
