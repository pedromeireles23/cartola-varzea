using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationInvitations",
                schema: "organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvitedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    InvitedEmailNormalized = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationInvitations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationInvitations_Users_AcceptedByUserId",
                        column: x => x.AcceptedByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationInvitations_Users_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_AcceptedByUserId",
                schema: "organizations",
                table: "OrganizationInvitations",
                column: "AcceptedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_InvitedByUserId",
                schema: "organizations",
                table: "OrganizationInvitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_OrganizationId_InvitedEmailNormalized_Status",
                schema: "organizations",
                table: "OrganizationInvitations",
                columns: new[] { "OrganizationId", "InvitedEmailNormalized", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_TokenHash",
                schema: "organizations",
                table: "OrganizationInvitations",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationInvitations",
                schema: "organizations");
        }
    }
}
