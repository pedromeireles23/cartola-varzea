using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Infrastructure.Identity;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Fronteira de autorização e idempotência da aprovação de organizadores.</summary>
public sealed class OrganizerApplicationFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task CommonUserAppliesAndAdminApprovalCreatesOneOrganization()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var applicantEmail = UniqueEmail("applicant");
        var adminEmail = UniqueEmail("admin");
        var applicantId = await CreateUserAsync(factory, applicantEmail);
        await CreateUserAsync(factory, adminEmail, ApplicationRole.PlatformAdmin);

        using var applicant = await CreateAuthenticatedClientAsync(
            factory, applicantEmail, cancellationToken);

        using var submitted = await applicant.PostAsJsonAsync(
            new Uri("/api/v1/organizer-applications", UriKind.Relative),
            new { organizationName = "Liga do Bairro" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var application = await submitted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var applicationId = application.GetProperty("id").GetGuid();

        using var duplicate = await applicant.PostAsJsonAsync(
            new Uri("/api/v1/organizer-applications", UriKind.Relative),
            new { organizationName = "Outro nome não deve duplicar" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var duplicateBody = await duplicate.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(applicationId, duplicateBody.GetProperty("id").GetGuid());

        using var forbiddenQueue = await applicant.GetAsync(
            new Uri("/api/v1/platform-admin/organizer-applications/pending", UriKind.Relative),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenQueue.StatusCode);

        using var admin = await CreateAuthenticatedClientAsync(factory, adminEmail, cancellationToken);
        using var queue = await admin.GetAsync(
            new Uri("/api/v1/platform-admin/organizer-applications/pending", UriKind.Relative),
            cancellationToken);
        queue.EnsureSuccessStatusCode();
        var pending = await queue.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var pendingItem = Assert.Single(
            pending.EnumerateArray(), item => item.GetProperty("id").GetGuid() == applicationId);
        // Quem decide precisa saber quem pediu; a fila só é visível ao Platform admin.
        Assert.Equal(applicantEmail, pendingItem.GetProperty("applicantEmail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(pendingItem.GetProperty("applicantDisplayName").GetString()));
        Assert.Equal("Liga do Bairro", pendingItem.GetProperty("organizationName").GetString());

        var review = new { reason = "Responsável validado para operar o campeonato." };
        using var approved = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/platform-admin/organizer-applications/{applicationId}/approve", UriKind.Relative),
            review,
            cancellationToken);
        approved.EnsureSuccessStatusCode();
        var approvedBody = await approved.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var organizationId = approvedBody.GetProperty("organizationId").GetGuid();

        using var repeated = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/platform-admin/organizer-applications/{applicationId}/approve", UriKind.Relative),
            review,
            cancellationToken);
        repeated.EnsureSuccessStatusCode();
        var repeatedBody = await repeated.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(organizationId, repeatedBody.GetProperty("organizationId").GetGuid());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(1, await dbContext.Organizations.CountAsync(
            item => item.Id == organizationId, cancellationToken));
        Assert.Equal(1, await dbContext.OrganizationMembers.CountAsync(
            item => item.OrganizationId == organizationId
                && item.UserId == applicantId
                && item.Role == OrganizationRole.Owner,
            cancellationToken));
        Assert.Equal(1, await dbContext.AdministrativeAuditEntries.CountAsync(
            item => item.TargetId == applicationId, cancellationToken));

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var applicantUser = await users.FindByIdAsync(applicantId.ToString());
        Assert.NotNull(applicantUser);
        Assert.True(await users.IsInRoleAsync(applicantUser, ApplicationRole.Organizer));
    }

    [Fact]
    public async Task RejectionIsAuditedAndCannotBecomeApproval()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var applicantEmail = UniqueEmail("rejected-applicant");
        var adminEmail = UniqueEmail("rejection-admin");
        await CreateUserAsync(factory, applicantEmail);
        await CreateUserAsync(factory, adminEmail, ApplicationRole.PlatformAdmin);

        using var applicant = await CreateAuthenticatedClientAsync(
            factory, applicantEmail, cancellationToken);
        using var submitted = await applicant.PostAsJsonAsync(
            new Uri("/api/v1/organizer-applications", UriKind.Relative),
            new { organizationName = "Liga sem documentação" },
            cancellationToken);
        submitted.EnsureSuccessStatusCode();
        var submittedBody = await submitted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var applicationId = submittedBody.GetProperty("id").GetGuid();

        using var admin = await CreateAuthenticatedClientAsync(factory, adminEmail, cancellationToken);
        var review = new { reason = "Documentação mínima ainda não apresentada." };
        using var rejected = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/platform-admin/organizer-applications/{applicationId}/reject", UriKind.Relative),
            review,
            cancellationToken);
        rejected.EnsureSuccessStatusCode();

        using var repeated = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/platform-admin/organizer-applications/{applicationId}/reject", UriKind.Relative),
            review,
            cancellationToken);
        repeated.EnsureSuccessStatusCode();

        using var conflictingApproval = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/platform-admin/organizer-applications/{applicationId}/approve", UriKind.Relative),
            new { reason = "Tentativa posterior incompatível." },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, conflictingApproval.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(1, await dbContext.AdministrativeAuditEntries.CountAsync(
            item => item.TargetId == applicationId, cancellationToken));
        Assert.Equal(0, await dbContext.Organizations.CountAsync(
            item => item.Name == "Liga sem documentação", cancellationToken));
    }

    [Fact]
    public async Task ConcurrentApprovalsDoNotBlockEachOther()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var adminEmail = UniqueEmail("concurrent-admin");
        await CreateUserAsync(factory, adminEmail, ApplicationRole.PlatformAdmin);

        var applicationIds = new List<Guid>();
        for (var index = 0; index < 6; index++)
        {
            var applicantEmail = UniqueEmail($"concurrent-applicant-{index}");
            await CreateUserAsync(factory, applicantEmail);
            using var applicant = await CreateAuthenticatedClientAsync(factory, applicantEmail, cancellationToken);
            using var submitted = await applicant.PostAsJsonAsync(
                new Uri("/api/v1/organizer-applications", UriKind.Relative),
                new { organizationName = $"Liga Concorrente {index}" },
                cancellationToken);
            submitted.EnsureSuccessStatusCode();
            var body = await submitted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            applicationIds.Add(body.GetProperty("id").GetGuid());
        }

        using var admin = await CreateAuthenticatedClientAsync(factory, adminEmail, cancellationToken);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var responses = await Task.WhenAll(applicationIds.Select(id => admin.PostAsJsonAsync(
            new Uri($"/api/v1/platform-admin/organizer-applications/{id}/approve", UriKind.Relative),
            new { reason = "Aprovações simultâneas." },
            cancellationToken)));
        stopwatch.Stop();
        // Com Serializable, deadlocks e retries levavam ~16 s. O detector de deadlock do
        // SQL Server roda a cada 5 s, então ficar abaixo disso indica que não houve espera.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Aprovações simultâneas levaram {stopwatch.ElapsedMilliseconds} ms.");

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(6, await dbContext.OrganizerApplications.CountAsync(
            item => applicationIds.Contains(item.Id) && item.OrganizationId != null, cancellationToken));

        // O mesmo pedido aprovado três vezes ao mesmo tempo cria uma única organização.
        var repeatedEmail = UniqueEmail("concurrent-repeated");
        await CreateUserAsync(factory, repeatedEmail);
        using var repeatedApplicant = await CreateAuthenticatedClientAsync(factory, repeatedEmail, cancellationToken);
        using var repeatedSubmission = await repeatedApplicant.PostAsJsonAsync(
            new Uri("/api/v1/organizer-applications", UriKind.Relative),
            new { organizationName = "Liga Repetida" },
            cancellationToken);
        repeatedSubmission.EnsureSuccessStatusCode();
        var repeatedId = (await repeatedSubmission.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
            .GetProperty("id").GetGuid();

        var repeatedResponses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => admin.PostAsJsonAsync(
            new Uri($"/api/v1/platform-admin/organizer-applications/{repeatedId}/approve", UriKind.Relative),
            new { reason = "Aprovação repetida." },
            cancellationToken)));
        var organizationIds = new HashSet<Guid>();
        foreach (var response in repeatedResponses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            organizationIds.Add(body.GetProperty("organizationId").GetGuid());
            response.Dispose();
        }

        var organizationId = Assert.Single(organizationIds);
        Assert.Equal(1, await dbContext.Organizations.CountAsync(
            item => item.Name == "Liga Repetida", cancellationToken));
        Assert.Equal(1, await dbContext.OrganizationMembers.CountAsync(
            item => item.OrganizationId == organizationId, cancellationToken));
    }

    [Fact]
    public async Task SimultaneousApprovalAndRejectionKeepOneDecisionWithoutServerError()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var adminEmail = UniqueEmail("race-admin");
        await CreateUserAsync(factory, adminEmail, ApplicationRole.PlatformAdmin);
        using var admin = await CreateAuthenticatedClientAsync(factory, adminEmail, cancellationToken);

        for (var round = 0; round < 5; round++)
        {
            var applicantEmail = UniqueEmail($"race-applicant-{round}");
            await CreateUserAsync(factory, applicantEmail);
            using var applicant = await CreateAuthenticatedClientAsync(factory, applicantEmail, cancellationToken);
            using var submitted = await applicant.PostAsJsonAsync(
                new Uri("/api/v1/organizer-applications", UriKind.Relative),
                new { organizationName = $"Liga Disputada {round}" },
                cancellationToken);
            submitted.EnsureSuccessStatusCode();
            var applicationId = (await submitted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
                .GetProperty("id").GetGuid();

            var responses = await Task.WhenAll(
                admin.PostAsJsonAsync(
                    new Uri($"/api/v1/platform-admin/organizer-applications/{applicationId}/approve", UriKind.Relative),
                    new { reason = "Aprovação na disputa." },
                    cancellationToken),
                admin.PostAsJsonAsync(
                    new Uri($"/api/v1/platform-admin/organizer-applications/{applicationId}/reject", UriKind.Relative),
                    new { reason = "Recusa na disputa." },
                    cancellationToken));

            var statuses = responses.Select(response => response.StatusCode).OrderBy(status => status).ToArray();
            foreach (var response in responses)
            {
                response.Dispose();
            }

            // Uma decisão vence e a outra recebe conflito; nunca as duas, nunca 500.
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);

            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
            Assert.Equal(1, await dbContext.AdministrativeAuditEntries.CountAsync(
                item => item.TargetId == applicationId, cancellationToken));
        }
    }
}
