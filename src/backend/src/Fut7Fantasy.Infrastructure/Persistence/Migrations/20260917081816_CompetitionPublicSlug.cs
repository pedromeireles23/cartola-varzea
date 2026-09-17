using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionPublicSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                schema: "competitions",
                table: "Competitions",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_Slug",
                schema: "competitions",
                table: "Competitions",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");

            // O slug só existe a partir daqui, então um campeonato publicado antes desta
            // migration não tem endereço público e não passaria no CHECK abaixo. Ele volta
            // para rascunho: publicar de novo gera o endereço pelo caminho normal. Nenhum
            // ambiente real passou por isso — a publicação nasceu na migration anterior,
            // no mesmo dia, e o projeto ainda não foi implantado.
            migrationBuilder.Sql(
                """
                UPDATE [competitions].[Competitions]
                SET [Status] = 'Draft'
                WHERE [Status] = 'Published' AND [Slug] IS NULL
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Competitions_PublishedHasSlug",
                schema: "competitions",
                table: "Competitions",
                sql: "[Status] <> 'Published' OR [Slug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Competitions_Slug",
                schema: "competitions",
                table: "Competitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Competitions_PublishedHasSlug",
                schema: "competitions",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "Slug",
                schema: "competitions",
                table: "Competitions");
        }
    }
}
