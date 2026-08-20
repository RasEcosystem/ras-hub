using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RasHub.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRacStatusObservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "rac_available",
                table: "ras_gates",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rac_status_observed_at",
                table: "ras_gates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rac_version",
                table: "ras_gates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rac_available",
                table: "ras_gates");

            migrationBuilder.DropColumn(
                name: "rac_status_observed_at",
                table: "ras_gates");

            migrationBuilder.DropColumn(
                name: "rac_version",
                table: "ras_gates");
        }
    }
}
