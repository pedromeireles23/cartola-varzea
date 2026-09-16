using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Organizations;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Organizations;

public sealed class OrganizationService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser) : IOrganizationService
{
    public async Task<IReadOnlyList<MyOrganizationView>> GetMineAsync(CancellationToken cancellationToken)
    {
        // O escopo sai da sessão, nunca de um parâmetro: a lista é a própria prova
        // de associação que a interface usa para montar a navegação.
        var userId = currentUser.Id
            ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");

        var memberships = await (
            from member in dbContext.OrganizationMembers.AsNoTracking()
            join organization in dbContext.Organizations.AsNoTracking()
                on member.OrganizationId equals organization.Id
            where member.UserId == userId
            orderby organization.Name
            select new { organization.Id, organization.Name, member.Role, member.JoinedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. memberships.Select(item =>
            new MyOrganizationView(item.Id, item.Name, item.Role.ToString(), item.JoinedAt))];
    }
}
