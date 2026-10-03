using System.Globalization;
using System.Security.Claims;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AuthenticationOptions = Fut7Fantasy.Infrastructure.Options.AuthenticationOptions;

namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>
/// Guarda o ticket da sessão no banco, e o cookie passa a levar só a chave
/// (04-seguranca §5).
///
/// Sem isso o logout só apagava o cookie: como toda resposta autenticada reemite o
/// cookie, uma resposta disparada antes do logout e entregue depois dele recriava a
/// sessão no navegador. Agora o logout apaga a linha, e o cookie que chega atrasado
/// aponta para uma sessão que não existe mais. Encerrar só a sessão de quem saiu, e
/// não todas as da conta, importa na demonstração: os visitantes dividem uma conta.
/// </summary>
public sealed class DatabaseTicketStore(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<AuthenticationOptions> options) : ITicketStore
{
    /// <inheritdoc />
    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);

    /// <inheritdoc />
    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var now = clock.GetUtcNow();
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = UserIdOf(ticket),
            Ticket = TicketSerializer.Default.Serialize(ticket),
            ExpiresAt = ExpiresAt(ticket, now),
        };

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();

        // Cada entrada recolhe as sessões vencidas: o projeto não tem job agendado.
        await dbContext.UserSessions
            .Where(expired => expired.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.UserSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session.Id.ToString("N", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, CancellationToken.None);

    /// <inheritdoc />
    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (!TryParseKey(key, out var id))
        {
            return;
        }

        var serialized = TicketSerializer.Default.Serialize(ticket);
        var expiresAt = ExpiresAt(ticket, clock.GetUtcNow());

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();

        // Só atualiza, nunca insere: a renovação de uma resposta que estava em voo chega
        // depois do logout e não pode trazer a sessão de volta.
        await dbContext.UserSessions
            .Where(session => session.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.Ticket, serialized)
                    .SetProperty(session => session.ExpiresAt, expiresAt),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    /// <inheritdoc />
    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        if (!TryParseKey(key, out var id))
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var session = await dbContext.UserSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(found => found.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return session is null || session.ExpiresAt <= clock.GetUtcNow()
            ? null
            : TicketSerializer.Default.Deserialize(session.Ticket);
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (!TryParseKey(key, out var id))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        await dbContext.UserSessions
            .Where(session => session.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private DateTimeOffset ExpiresAt(AuthenticationTicket ticket, DateTimeOffset now) =>
        ticket.Properties.ExpiresUtc ?? now.Add(options.Value.SessionLifetime);

    private static Guid UserIdOf(AuthenticationTicket ticket) =>
        Guid.TryParse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw new InvalidOperationException("O ticket de sessão não identifica a conta.");

    private static bool TryParseKey(string key, out Guid id) =>
        Guid.TryParseExact(key, "N", out id);
}
