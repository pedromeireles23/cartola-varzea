using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Infrastructure.Identity;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Convites e isolamento da equipe de uma organização.</summary>
public sealed partial class OrganizationTeamFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private const string SenhaValida = "uma-senha-bem-longa-2026";

    [GeneratedRegex(@"https://testes\.local/organizar/convite\?token=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex InvitationToken { get; }

    [Fact]
    public async Task OwnerInvitesAndOnlyCorrectAccountAcceptsAsAssistant()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        var ownerEmail = UniqueEmail("owner");
        var assistantEmail = UniqueEmail("assistant");
        var outsiderEmail = UniqueEmail("outsider");
        var otherOwnerEmail = UniqueEmail("other-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        await CreateUserAsync(factory, outsiderEmail);
        var otherOwnerId = await CreateUserAsync(factory, otherOwnerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Autorizada");
        var otherOrganizationId = await CreateOrganizationAsync(factory, otherOwnerId, "Outra Liga");

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var invited = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/team/invitations", UriKind.Relative),
            new { email = assistantEmail },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var invitedBody = await invited.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var invitationId = invitedBody.GetProperty("id").GetGuid();
        var token = ExtractToken(emails.LastTo(assistantEmail).TextBody);

        using var crossOrganization = await owner.GetAsync(
            new Uri($"/api/v1/organizations/{otherOrganizationId}/team", UriKind.Relative),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, crossOrganization.StatusCode);

        using var outsider = await CreateAuthenticatedClientAsync(factory, outsiderEmail, cancellationToken);
        using var wrongAccount = await outsider.PostAsJsonAsync(
            new Uri("/api/v1/organization-invitations/accept", UriKind.Relative),
            new { token },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, wrongAccount.StatusCode);

        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        using var accepted = await assistant.PostAsJsonAsync(
            new Uri("/api/v1/organization-invitations/accept", UriKind.Relative),
            new { token },
            cancellationToken);
        accepted.EnsureSuccessStatusCode();

        using var acceptedAgain = await assistant.PostAsJsonAsync(
            new Uri("/api/v1/organization-invitations/accept", UriKind.Relative),
            new { token },
            cancellationToken);
        acceptedAgain.EnsureSuccessStatusCode();

        using var elevationAttempt = await assistant.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/team/invitations", UriKind.Relative),
            new { email = UniqueEmail("unauthorized") },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, elevationAttempt.StatusCode);

        using var teamResponse = await owner.GetAsync(
            new Uri($"/api/v1/organizations/{organizationId}/team", UriKind.Relative),
            cancellationToken);
        teamResponse.EnsureSuccessStatusCode();
        var team = await teamResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(organizationId, team.GetProperty("organizationId").GetGuid());
        Assert.Equal("Liga Autorizada", team.GetProperty("organizationName").GetString());
        Assert.Contains(
            team.GetProperty("assistants").EnumerateArray(),
            member => member.GetProperty("userId").GetGuid() == assistantId);
        Assert.Contains(
            team.GetProperty("invitations").EnumerateArray(),
            invitation => invitation.GetProperty("id").GetGuid() == invitationId
                && invitation.GetProperty("status").GetString() == "Accepted");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var persisted = await dbContext.OrganizationInvitations.AsNoTracking()
            .SingleAsync(item => item.Id == invitationId, cancellationToken);
        Assert.NotEqual(token, persisted.TokenHash);
        Assert.Equal(OrganizationRole.Assistant, await dbContext.OrganizationMembers
            .Where(member => member.OrganizationId == organizationId && member.UserId == assistantId)
            .Select(member => member.Role)
            .SingleAsync(cancellationToken));
        var auditActions = await dbContext.AdministrativeAuditEntries
            .Where(entry => entry.TargetId == invitationId)
            .Select(entry => entry.Action)
            .ToListAsync(cancellationToken);
        Assert.Contains("OrganizationAssistantInvited", auditActions);
        Assert.Contains("OrganizationAssistantInvitationAccepted", auditActions);
    }

    [Fact]
    public async Task RevokedInvitationCannotBeAccepted()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        var ownerEmail = UniqueEmail("revoking-owner");
        var assistantEmail = UniqueEmail("revoked-assistant");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        await CreateUserAsync(factory, assistantEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga que Revoga");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);

        using var invited = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/team/invitations", UriKind.Relative),
            new { email = assistantEmail },
            cancellationToken);
        invited.EnsureSuccessStatusCode();
        var body = await invited.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var invitationId = body.GetProperty("id").GetGuid();
        var token = ExtractToken(emails.LastTo(assistantEmail).TextBody);

        using var revoked = await owner.DeleteAsync(
            new Uri(
                $"/api/v1/organizations/{organizationId}/team/invitations/{invitationId}",
                UriKind.Relative),
            cancellationToken);
        revoked.EnsureSuccessStatusCode();

        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        using var accept = await assistant.PostAsJsonAsync(
            new Uri("/api/v1/organization-invitations/accept", UriKind.Relative),
            new { token },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var auditActions = await dbContext.AdministrativeAuditEntries
            .Where(entry => entry.TargetId == invitationId)
            .Select(entry => entry.Action)
            .ToListAsync(cancellationToken);
        Assert.Contains("OrganizationAssistantInvited", auditActions);
        Assert.Contains("OrganizationAssistantInvitationRevoked", auditActions);
    }

    [Fact]
    public async Task ExpiredInvitationCannotBeAccepted()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = sqlServer.CreateApi(emails, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var ownerEmail = UniqueEmail("expiring-owner");
        var assistantEmail = UniqueEmail("expired-assistant");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga com Prazo");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);

        using var invited = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/team/invitations", UriKind.Relative),
            new { email = assistantEmail },
            cancellationToken);
        invited.EnsureSuccessStatusCode();
        var token = ExtractToken(emails.LastTo(assistantEmail).TextBody);

        clock.Advance(TimeSpan.FromDays(8));

        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        using var accept = await assistant.PostAsJsonAsync(
            new Uri("/api/v1/organization-invitations/accept", UriKind.Relative),
            new { token },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Gone, accept.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.False(await dbContext.OrganizationMembers.AnyAsync(
            member => member.OrganizationId == organizationId && member.UserId == assistantId,
            cancellationToken));
    }

    [Fact]
    public async Task RemovedAssistantLosesAccessOnNextRequestAndCanBeInvitedAgain()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        var ownerEmail = UniqueEmail("removing-owner");
        var assistantEmail = UniqueEmail("removed-assistant");
        var otherAssistantEmail = UniqueEmail("other-assistant");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var otherAssistantId = await CreateUserAsync(factory, otherAssistantEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga que Remove");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
            dbContext.OrganizationMembers.Add(OrganizationMember.CreateAssistant(
                organizationId, assistantId, ApiFactory.FixedNow));
            dbContext.OrganizationMembers.Add(OrganizationMember.CreateAssistant(
                organizationId, otherAssistantId, ApiFactory.FixedNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var organizationUri = new Uri($"/api/v1/organizations/{organizationId}", UriKind.Relative);
        Uri AssistantUri(Guid userId) =>
            new($"/api/v1/organizations/{organizationId}/team/assistants/{userId}", UriKind.Relative);

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        using var otherAssistant = await CreateAuthenticatedClientAsync(
            factory, otherAssistantEmail, cancellationToken);

        // A sessão do auxiliar vê a organização antes da remoção.
        using (var before = await assistant.GetAsync(organizationUri, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
            var body = await before.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal("Assistant", body.GetProperty("role").GetString());
        }

        // Auxiliar não remove colega, e o proprietário não sai por esta rota.
        using (var byAssistant = await otherAssistant.DeleteAsync(AssistantUri(assistantId), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, byAssistant.StatusCode);
        }

        using (var removingOwner = await owner.DeleteAsync(AssistantUri(ownerId), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, removingOwner.StatusCode);
        }

        using (var removed = await owner.DeleteAsync(AssistantUri(assistantId), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        using (var removedAgain = await owner.DeleteAsync(AssistantUri(assistantId), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, removedAgain.StatusCode);
        }

        // A mesma sessão, sem sair e entrar, já perde o acesso.
        using (var after = await assistant.GetAsync(organizationUri, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
            Assert.Equal(1, await dbContext.AdministrativeAuditEntries.CountAsync(
                entry => entry.TargetId == assistantId && entry.Action == "OrganizationAssistantRemoved",
                cancellationToken));
            Assert.True(await dbContext.OrganizationMembers.AnyAsync(
                member => member.OrganizationId == organizationId && member.UserId == otherAssistantId,
                cancellationToken));
        }

        // Voltar exige um convite novo, e ele funciona normalmente.
        using (var invited = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/team/invitations", UriKind.Relative),
            new { email = assistantEmail },
            cancellationToken))
        {
            invited.EnsureSuccessStatusCode();
        }

        var token = ExtractToken(emails.LastTo(assistantEmail).TextBody);
        using (var accepted = await assistant.PostAsJsonAsync(
            new Uri("/api/v1/organization-invitations/accept", UriKind.Relative),
            new { token },
            cancellationToken))
        {
            accepted.EnsureSuccessStatusCode();
        }

        using var again = await assistant.GetAsync(organizationUri, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task MyOrganizationsListsOnlyMembershipsOfCurrentAccount()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("listing-owner");
        var assistantEmail = UniqueEmail("listing-assistant");
        var outsiderEmail = UniqueEmail("listing-outsider");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var outsiderId = await CreateUserAsync(factory, outsiderEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Listada");
        var otherOrganizationId = await CreateOrganizationAsync(factory, outsiderId, "Liga Alheia");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
            dbContext.OrganizationMembers.Add(OrganizationMember.CreateAssistant(
                organizationId, assistantId, ApiFactory.FixedNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        using var anonymous = factory.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync(
            new Uri("/api/v1/organizations/mine", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var ownerOrganizations = await GetMyOrganizationsAsync(owner, cancellationToken);
        var ownerOrganization = Assert.Single(ownerOrganizations);
        Assert.Equal(organizationId, ownerOrganization.GetProperty("id").GetGuid());
        Assert.Equal("Owner", ownerOrganization.GetProperty("role").GetString());

        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var assistantOrganization = Assert.Single(await GetMyOrganizationsAsync(assistant, cancellationToken));
        Assert.Equal(organizationId, assistantOrganization.GetProperty("id").GetGuid());
        Assert.Equal("Assistant", assistantOrganization.GetProperty("role").GetString());

        using var outsider = await CreateAuthenticatedClientAsync(factory, outsiderEmail, cancellationToken);
        var outsiderOrganization = Assert.Single(await GetMyOrganizationsAsync(outsider, cancellationToken));
        Assert.Equal(otherOrganizationId, outsiderOrganization.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task DemoViewerCannotWriteByCallingApiDirectly()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var email = UniqueEmail("demo");
        await CreateUserAsync(factory, email, ApplicationRole.DemoViewer);
        using var demo = await CreateAuthenticatedClientAsync(factory, email, cancellationToken);

        using var response = await demo.PostAsJsonAsync(
            new Uri("/api/v1/organizer-applications", UriKind.Relative),
            new { organizationName = "Tentativa da demonstração" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("Modo demonstração", problem.GetProperty("title").GetString());
        Assert.Equal("demo_read_only", problem.GetProperty("code").GetString());
    }

    private static async Task<Guid> CreateUserAsync(
        WebApplicationFactory<Program> factory,
        string email,
        string? role = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Pessoa de Teste",
            CreatedAt = ApiFactory.FixedNow,
        };
        Assert.True((await users.CreateAsync(user, SenhaValida)).Succeeded);

        if (role is not null)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                Assert.True((await roles.CreateAsync(new ApplicationRole
                {
                    Id = Guid.CreateVersion7(),
                    Name = role,
                })).Succeeded);
            }

            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }

        return user.Id;
    }

    private static async Task<Guid> CreateOrganizationAsync(
        WebApplicationFactory<Program> factory,
        Guid ownerId,
        string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var organization = Organization.Create(Guid.CreateVersion7(), name, ApiFactory.FixedNow);
        dbContext.Organizations.Add(organization);
        dbContext.OrganizationMembers.Add(OrganizationMember.CreateOwner(
            organization.Id, ownerId, ApiFactory.FixedNow));
        await dbContext.SaveChangesAsync();
        return organization.Id;
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string email,
        CancellationToken cancellationToken)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RefreshAntiforgeryAsync(client, cancellationToken);
        using var login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = SenhaValida },
            cancellationToken);
        login.EnsureSuccessStatusCode();
        await RefreshAntiforgeryAsync(client, cancellationToken);
        return client;
    }

    private static async Task RefreshAntiforgeryAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri("/api/v1/auth/antiforgery", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        var requestToken = response.Headers.GetValues("Set-Cookie")
            .Select(cookie => Regex.Match(cookie, @"XSRF-TOKEN=(?<value>[^;]+)"))
            .First(match => match.Success)
            .Groups["value"].Value;
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", HttpUtility.UrlDecode(requestToken));
    }

    private static async Task<List<JsonElement>> GetMyOrganizationsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var organizations = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/v1/organizations/mine", UriKind.Relative), cancellationToken);
        return [.. organizations.EnumerateArray()];
    }

    private static string ExtractToken(string body)
    {
        var match = InvitationToken.Match(body);
        Assert.True(match.Success, $"Nenhum token de convite encontrado em: {body}");
        return match.Groups["token"].Value;
    }

    private static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@example.test";
}
