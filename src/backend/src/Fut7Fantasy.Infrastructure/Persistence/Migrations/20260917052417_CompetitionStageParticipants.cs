using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionStageParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StageParticipants",
                schema: "competitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageParticipants_RealTeams_RealTeamId",
                        column: x => x.RealTeamId,
                        principalSchema: "sports_catalog",
                        principalTable: "RealTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StageParticipants_StageGroups_StageGroupId",
                        column: x => x.StageGroupId,
                        principalSchema: "competitions",
                        principalTable: "StageGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StageParticipants_Stages_StageId",
                        column: x => x.StageId,
                        principalSchema: "competitions",
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StageParticipants_RealTeamId",
                schema: "competitions",
                table: "StageParticipants",
                column: "RealTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_StageParticipants_StageGroupId",
                schema: "competitions",
                table: "StageParticipants",
                column: "StageGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StageParticipants_StageId_RealTeamId",
                schema: "competitions",
                table: "StageParticipants",
                columns: new[] { "StageId", "RealTeamId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageParticipants",
                schema: "competitions");
        }
    }
}
