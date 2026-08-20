using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RasHub.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRasInfobases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ras_infobases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ras_cluster_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ras_infobases", x => x.id);
                    table.ForeignKey(
                        name: "fk_ras_infobases_ras_clusters_ras_cluster_id",
                        column: x => x.ras_cluster_id,
                        principalTable: "ras_clusters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_ras_infobases_ras_cluster_id_external_id",
                table: "ras_infobases",
                columns: new[] { "ras_cluster_id", "external_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ras_infobases");
        }
    }
}
