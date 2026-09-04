using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RasHub.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRasEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM ras_infobases;
                DELETE FROM ras_clusters;

                ALTER TABLE ras_clusters
                    DROP CONSTRAINT IF EXISTS fk_ras_clusters_ras_gates_ras_gate_id;
                ALTER TABLE ras_clusters
                    DROP CONSTRAINT IF EXISTS fk_ras_clusters_ras_endpoints_ras_endpoint_id;

                DROP TABLE IF EXISTS ras_endpoint_routes;
                DROP TABLE IF EXISTS ras_endpoints;

                DROP INDEX IF EXISTS ux_ras_clusters_ras_gate_id_external_id;
                DROP INDEX IF EXISTS ux_ras_clusters_ras_endpoint_id_external_id;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'ras_clusters'
                          AND column_name = 'ras_gate_id') THEN
                        ALTER TABLE ras_clusters
                            RENAME COLUMN ras_gate_id TO ras_endpoint_id;
                    END IF;
                END
                $$;
                """,
                suppressTransaction: false);

            migrationBuilder.CreateTable(
                name: "ras_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ras_gate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    configuration_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ras_endpoints", x => x.id);
                    table.CheckConstraint("ck_ras_endpoints_port", "port BETWEEN 1 AND 65535");
                    table.ForeignKey(
                        name: "fk_ras_endpoints_ras_gates_ras_gate_id",
                        column: x => x.ras_gate_id,
                        principalTable: "ras_gates",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ras_endpoints_ras_gate_id",
                table: "ras_endpoints",
                column: "ras_gate_id");

            migrationBuilder.CreateIndex(
                name: "ux_ras_clusters_ras_endpoint_id_external_id",
                table: "ras_clusters",
                columns: new[] { "ras_endpoint_id", "external_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_ras_clusters_ras_endpoints_ras_endpoint_id",
                table: "ras_clusters",
                column: "ras_endpoint_id",
                principalTable: "ras_endpoints",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM ras_infobases;
                DELETE FROM ras_clusters;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_ras_clusters_ras_endpoints_ras_endpoint_id",
                table: "ras_clusters");

            migrationBuilder.DropTable(
                name: "ras_endpoints");

            migrationBuilder.RenameColumn(
                name: "ras_endpoint_id",
                table: "ras_clusters",
                newName: "ras_gate_id");

            migrationBuilder.RenameIndex(
                name: "ux_ras_clusters_ras_endpoint_id_external_id",
                table: "ras_clusters",
                newName: "ux_ras_clusters_ras_gate_id_external_id");

            migrationBuilder.AddForeignKey(
                name: "fk_ras_clusters_ras_gates_ras_gate_id",
                table: "ras_clusters",
                column: "ras_gate_id",
                principalTable: "ras_gates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
