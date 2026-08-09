using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RasHub.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddExtendedRasClusterFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_access_right_audit_events_recording",
                table: "ras_clusters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "kill_by_memory_with_dump",
                table: "ras_clusters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "ping_period",
                table: "ras_clusters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ping_timeout",
                table: "ras_clusters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "restart_schedule",
                table: "ras_clusters",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allow_access_right_audit_events_recording",
                table: "ras_clusters");

            migrationBuilder.DropColumn(
                name: "kill_by_memory_with_dump",
                table: "ras_clusters");

            migrationBuilder.DropColumn(
                name: "ping_period",
                table: "ras_clusters");

            migrationBuilder.DropColumn(
                name: "ping_timeout",
                table: "ras_clusters");

            migrationBuilder.DropColumn(
                name: "restart_schedule",
                table: "ras_clusters");
        }
    }
}
