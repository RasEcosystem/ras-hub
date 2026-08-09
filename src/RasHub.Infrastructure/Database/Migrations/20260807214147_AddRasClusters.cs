using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RasHub.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRasClusters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
