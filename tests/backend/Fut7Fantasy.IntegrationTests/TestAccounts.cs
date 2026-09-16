using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Web;
using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Infrastructure.Identity;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Contas, organizações e sessões para os testes de fluxo contra o SQL Server real.
/// Os dados entram direto pelo banco; o que se testa é a API que vem depois.
/// </summary>
internal static partial class TestAccounts
{
    public const string ValidPassword = "uma-senha-bem-longa-2026";

    [GeneratedRegex("XSRF-TOKEN=(?<value>[^;]+)")]
    private static partial Regex AntiforgeryCookie { get; }

    /// <summary>E-mail único por teste, para que as classes não disputem as mesmas contas.</summary>
    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>Conta com e-mail confirmado e, opcionalmente, um papel global.</summary>
    public static async Task<Guid> CreateUserAsync(
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
        Assert.True((await users.CreateAsync(user, ValidPassword)).Succeeded);

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

    /// <summary>Organização com a conta informada como proprietária.</summary>
    public static async Task<Guid> CreateOrganizationAsync(
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

    /// <summary>Associa uma conta como auxiliar, sem passar pelo convite.</summary>
    public static async Task AddAssistantAsync(
        WebApplicationFactory<Program> factory,
        Guid organizationId,
        Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        dbContext.OrganizationMembers.Add(OrganizationMember.CreateAssistant(
            organizationId, userId, ApiFactory.FixedNow));
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Cliente com sessão aberta e token antiforgery pronto para escrever.</summary>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string email,
        CancellationToken cancellationToken)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RefreshAntiforgeryAsync(client, cancellationToken);
        using var login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = ValidPassword },
            cancellationToken);
        login.EnsureSuccessStatusCode();
        await RefreshAntiforgeryAsync(client, cancellationToken);
        return client;
    }

    /// <summary>Busca o token antiforgery e o repete no cabeçalho, como o Angular faz.</summary>
    public static async Task RefreshAntiforgeryAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri("/api/v1/auth/antiforgery", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        var requestToken = response.Headers.GetValues("Set-Cookie")
            .Select(cookie => AntiforgeryCookie.Match(cookie))
            .First(match => match.Success)
            .Groups["value"].Value;
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", HttpUtility.UrlDecode(requestToken));
    }
}
