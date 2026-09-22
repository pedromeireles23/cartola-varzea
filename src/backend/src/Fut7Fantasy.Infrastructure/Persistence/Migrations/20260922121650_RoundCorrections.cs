using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoundCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                schema: "competitions",
                table: "Rounds",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReopenedAt",
                schema: "competitions",
                table: "Rounds",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                schema: "scoring",
                table: "RoundCalculations",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                schema: "competitions",
                table: "Rounds");

            migrationBuilder.DropColumn(
                name: "ReopenedAt",
                schema: "competitions",
                table: "Rounds");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                schema: "scoring",
                table: "RoundCalculations");
        }
    }
}
