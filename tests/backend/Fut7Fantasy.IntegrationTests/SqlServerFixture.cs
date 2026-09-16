using Fut7Fantasy.Infrastructure.Options;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// SQL Server real em container, com as migrations aplicadas. A imagem e a mesma do
/// compose.yaml. Sem Docker a fixture nao falha: os testes que dependem dela sao
/// ignorados com o motivo, porque o ambiente principal do projeto e Windows e nem
/// sempre tem o Docker Desktop ligado. No CI o Docker existe e os testes rodam.
/// </summary>
public sealed class SqlServerFixture : Xunit.IAsyncLifetime
{
    private const string Image =
        "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04"
        + "@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090";

    private MsSqlContainer? _container;

    /// <summary>Motivo pelo qual o container nao subiu, ou <c>null</c> quando subiu.</summary>
    public string? Unavailable { get; private set; }

    /// <summary>Connection string do container.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        try
        {
            // Build() ja valida o endpoint do Docker, por isso fica dentro do try.
            _container = new MsSqlBuilder(Image).Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();

            // A aplicacao nao aplica migration ao subir; quem prepara o schema e o teste.
            var options = new DbContextOptionsBuilder<Fut7FantasyDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;
            await using var dbContext = new Fut7FantasyDbContext(options);
            await dbContext.Database.MigrateAsync();
        }
#pragma warning disable CA1031 // Ausencia de Docker vira teste ignorado, nao falha de suite.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Unavailable = $"SQL Server em container indisponivel: {exception.Message}";
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Cria um host apontando para o container, com a infraestrutura real.</summary>
    public WebApplicationFactory<Program> CreateApi() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting(
                $"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.ConnectionString)}",
                ConnectionString));
}
