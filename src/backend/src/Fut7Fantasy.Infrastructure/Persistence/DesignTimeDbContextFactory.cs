using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fut7Fantasy.Infrastructure.Persistence;

/// <summary>
/// Usada por <c>dotnet ef</c>. Le a connection string da variavel de ambiente
/// <c>Database__ConnectionString</c>, a mesma chave de configuracao da aplicacao.
///
/// Sem a variavel definida, cai num placeholder que nunca abre conexao: gerar uma
/// migration nao deve exigir banco no ar nem segredo. Aplicar a migration, sim.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<Fut7FantasyDbContext>
{
    private const string ConnectionStringVariable = "Database__ConnectionString";

    private const string PlaceholderConnectionString =
        "Server=design-time;Database=Fut7Fantasy;Trusted_Connection=True;";

    /// <inheritdoc />
    public Fut7FantasyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = PlaceholderConnectionString;
        }

        var options = new DbContextOptionsBuilder<Fut7FantasyDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", Fut7FantasyDbContext.Schema))
            .Options;

        return new Fut7FantasyDbContext(options);
    }
}
