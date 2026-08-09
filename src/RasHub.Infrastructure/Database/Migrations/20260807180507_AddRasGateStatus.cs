using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RasHub.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRasGateStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "instance_name",
                table: "ras_gates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "status_observed_at",
                table: "ras_gates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "version",
                table: "ras_gates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "instance_name",
                table: "ras_gates");

            migrationBuilder.DropColumn(
                name: "status_observed_at",
                table: "ras_gates");

            migrationBuilder.DropColumn(
                name: "version",
                table: "ras_gates");
        }
    }
}
