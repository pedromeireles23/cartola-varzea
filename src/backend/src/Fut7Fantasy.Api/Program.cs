using System.Diagnostics;
using System.Globalization;
using System.Threading.RateLimiting;
using Fut7Fantasy.Api.Endpoints;
using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application;
using Fut7Fantasy.Application.Diagnostics;
using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Infrastructure;
using Fut7Fantasy.Infrastructure.Authorization;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

builder.Services.AddOpenApi();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHealthChecks().AddInfrastructureHealthChecks();

// Sessao em cookie: nada de token no browser, e o cookie e inacessivel ao script.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "fut7fantasy.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;

    // A API responde status; quem decide para onde navegar e o frontend.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services
    .AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
    .Configure<IOptions<Fut7Fantasy.Infrastructure.Options.AuthenticationOptions>>(
        (cookie, auth) => cookie.ExpireTimeSpan = auth.Value.SessionLifetime);

// Antiforgery no formato que o HttpClient do Angular já envia por padrão: ele lê o
// cookie XSRF-TOKEN e repete o valor no cabeçalho X-XSRF-TOKEN.
//
// O cookie do antiforgery em si continua HttpOnly e com o nome padrão; quem vai
// para o cookie legível é o *request token*, que é outro valor. Trocar o nome do
// cookie interno por XSRF-TOKEN enviaria metade do par duplicada e o par deixaria
// de proteger contra nada.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthorizationPolicies.PlatformAdmin,
        policy => policy.RequireRole(ApplicationRole.PlatformAdmin));
    options.AddPolicy(
        AuthorizationPolicies.OrganizationMember,
        policy => policy.RequireAuthenticatedUser().AddRequirements(
            new OrganizationRoleRequirement(OrganizationRole.Owner, OrganizationRole.Assistant)));
    options.AddPolicy(
        AuthorizationPolicies.OrganizationOwner,
        policy => policy.RequireAuthenticatedUser().AddRequirements(
            new OrganizationRoleRequirement(OrganizationRole.Owner)));
    options.AddPolicy(
        AuthorizationPolicies.OrganizationOwnerWrite,
        policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.IsInRole(ApplicationRole.DemoViewer))
            .AddRequirements(new OrganizationRoleRequirement(OrganizationRole.Owner)));
    options.AddPolicy(
        AuthorizationPolicies.CompetitionMember,
        policy => policy.RequireAuthenticatedUser().AddRequirements(
            new CompetitionRoleRequirement(OrganizationRole.Owner, OrganizationRole.Assistant)));
    options.AddPolicy(
        AuthorizationPolicies.CompetitionOwnerWrite,
        policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.IsInRole(ApplicationRole.DemoViewer))
            .AddRequirements(new CompetitionRoleRequirement(OrganizationRole.Owner)));
    options.AddPolicy(
        AuthorizationPolicies.AuthenticatedWrite,
        policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.IsInRole(ApplicationRole.DemoViewer)));
});

// Rate limiting por risco de endpoint (04-seguranca §13). A chave combina IP e
// caminho: sem isso, uma janela por IP deixaria o abuso de login consumir a cota
// de navegacao normal.
builder.Services.AddOptions<AccountRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(AccountRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<GlobalRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(GlobalRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(AccountEndpoints.StrictRateLimitPolicy, context =>
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<AccountRateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"{context.Connection.RemoteIpAddress}|{context.Request.Path}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.PermitLimit,
                Window = limits.Window,
            });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<GlobalRateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.PermitLimit,
                Window = limits.Window,
            });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        // Informa quando tentar de novo, sem revelar a regra exata.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await context.HttpContext.Response
            .WriteAsJsonAsync(
                new { title = "Muitas tentativas", status = 429 },
                cancellationToken)
            .ConfigureAwait(false);
    };
});

var app = builder.Build();

// Falhas inesperadas e respostas de erro sem corpo viram Problem Details, sem detalhes internos.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.UseRateLimiter();

app.UseAuthentication();
app.UseDemoViewerReadOnly();
app.UseAuthorization();
app.UseAntiforgeryForMutations();

// Liveness responde pelo processo e nao consulta dependencia alguma.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness responde pela capacidade de atender, e por isso inclui o banco.
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckTags.Readiness) });

app.MapGet(
        "/api/v1/system/info",
        async (GetSystemInfo getSystemInfo, CancellationToken cancellationToken) =>
            await getSystemInfo.ExecuteAsync(cancellationToken))
    .WithName("GetSystemInfo")
    .WithSummary("Informação técnica pública da aplicação.");

app.MapAccountEndpoints();
app.MapOrganizerApplicationEndpoints();
app.MapOrganizationTeamEndpoints();
app.MapCompetitionEndpoints();
app.MapPublicCompetitionEndpoints();
app.MapCompetitionStageEndpoints();
app.MapRealTeamEndpoints();
app.MapAthleteEndpoints();
app.MapCoachEndpoints();
app.MapCatalogImportEndpoints();

await app.RunAsync();

/// <summary>Ponto de entrada exposto para testes de integração.</summary>
public partial class Program;
