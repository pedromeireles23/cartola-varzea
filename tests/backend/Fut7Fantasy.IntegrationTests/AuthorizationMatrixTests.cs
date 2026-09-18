using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Matriz de autorização da API (04-seguranca §6).
///
/// Compara todas as rotas registradas com a matriz esperada. Uma rota nova, ou uma
/// policy trocada, faz o teste falhar até que a matriz, e o documento de segurança,
/// sejam atualizados de propósito. Os testes de fluxo provam o comportamento de cada
/// policy; este garante que nenhuma rota ficou sem decisão explícita.
/// </summary>
public sealed class AuthorizationMatrixTests
{
    private const string Anonymous = "anônimo";
    private const string Authenticated = "autenticado";

    /// <summary>
    /// Método, rota e exigência. Além do que aparece aqui, dois middlewares valem para
    /// toda a API: antiforgery em POST/PUT/PATCH/DELETE e bloqueio de escrita para
    /// DemoViewer (exceto logout).
    /// </summary>
    private static readonly string[] ExpectedMatrix =
    [
        "* /health/live -> " + Anonymous,
        "* /health/ready -> " + Anonymous,
        "DELETE /api/v1/competitions/{competitionId:guid}/athletes/{athleteId:guid} -> competition-owner-write",
        "DELETE /api/v1/fantasy/{slug}/squad/{kind}/{assetId:guid} -> authenticated-write",
        "DELETE /api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid} -> competition-owner-write",
        "DELETE /api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid}"
            + "/matches/{matchId:guid} -> competition-owner-write",
        "DELETE /api/v1/competitions/{competitionId:guid}/stages/{stageId:guid} -> competition-owner-write",
        "DELETE /api/v1/competitions/{competitionId:guid}/teams/{teamId:guid} -> competition-owner-write",
        "DELETE /api/v1/organizations/{organizationId:guid}/team/assistants/{userId:guid} -> organization-owner-write",
        "DELETE /api/v1/organizations/{organizationId:guid}/team/invitations/{invitationId:guid}"
            + " -> organization-owner-write",
        "GET /api/v1/auth/antiforgery -> " + Anonymous,
        "GET /api/v1/competitions/{competitionId:guid}/athletes/ -> competition-member",
        "GET /api/v1/competitions/{competitionId:guid}/coaches/ -> competition-member",
        "GET /api/v1/competitions/{competitionId:guid}/imports/{kind}/template -> competition-member",
        "GET /api/v1/competitions/{competitionId:guid}/matches/{matchId:guid}/sheet/ -> competition-member",
        "GET /api/v1/competitions/{competitionId:guid}/readiness -> competition-member",
        "GET /api/v1/competitions/{competitionId:guid}/rounds/ -> competition-member",
        "GET /api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid}/imports/estatisticas/template"
            + " -> competition-member",
        "GET /api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid}/review -> competition-member",
        "GET /api/v1/competitions/{competitionId:guid}/settings -> competition-member",
        "GET /api/v1/competitions/{competitionId:guid}/stages/ -> competition-member",
        "GET /api/v1/competitions/{competitionId:guid}/teams/ -> competition-member",
        "GET /api/v1/fantasy/{slug}/ -> " + Authenticated,
        "GET /api/v1/fantasy/{slug}/market -> " + Authenticated,
        "GET /api/v1/import-templates -> " + Anonymous,
        "GET /api/v1/modality-profiles -> " + Anonymous,
        "GET /api/v1/public/competitions/ -> " + Anonymous,
        "GET /api/v1/public/competitions/{slug} -> " + Anonymous,
        "GET /api/v1/organizations/{organizationId:guid}/competitions/ -> organization-member",
        "GET /api/v1/auth/me -> " + Anonymous,
        "GET /api/v1/organizations/mine -> " + Authenticated,
        "GET /api/v1/organizations/{organizationId:guid} -> organization-member",
        "GET /api/v1/organizations/{organizationId:guid}/team/ -> organization-owner",
        "GET /api/v1/organizer-applications/mine -> " + Authenticated,
        "GET /api/v1/platform-admin/organizer-applications/pending -> platform-admin",
        "GET /api/v1/system/info -> " + Anonymous,
        "GET /openapi/{documentName}.json -> " + Anonymous,
        "POST /api/v1/competitions/{competitionId:guid}/athletes/ -> competition-owner-write",
        "POST /api/v1/competitions/{competitionId:guid}/imports/{kind} -> competition-owner-write",
        "POST /api/v1/competitions/{competitionId:guid}/imports/{kind}/preview -> competition-owner-write",
        "POST /api/v1/competitions/{competitionId:guid}/rounds/ -> competition-owner-write",
        "POST /api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid}/imports/estatisticas/"
            + " -> competition-staff-write",
        "POST /api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid}/imports/estatisticas/preview"
            + " -> competition-staff-write",
        "POST /api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid}/matches -> competition-owner-write",
        "POST /api/v1/competitions/{competitionId:guid}/stages/ -> competition-owner-write",
        "POST /api/v1/competitions/{competitionId:guid}/teams/ -> competition-owner-write",
        "POST /api/v1/auth/confirm-email -> " + Anonymous,
        "POST /api/v1/auth/forgot-password -> " + Anonymous,
        "POST /api/v1/auth/login -> " + Anonymous,
        "POST /api/v1/auth/logout -> " + Authenticated,
        "POST /api/v1/auth/register -> " + Anonymous,
        "POST /api/v1/auth/reset-password -> " + Anonymous,
        "POST /api/v1/fantasy/{slug}/entry -> authenticated-write",
        "POST /api/v1/fantasy/{slug}/squad/{kind}/{assetId:guid} -> authenticated-write",
        "POST /api/v1/organization-invitations/accept -> authenticated-write",
        "POST /api/v1/organizations/{organizationId:guid}/competitions/ -> organization-owner-write",
        "POST /api/v1/organizations/{organizationId:guid}/team/invitations -> organization-owner-write",
        "POST /api/v1/organizer-applications/ -> " + Authenticated,
        "POST /api/v1/platform-admin/organizer-applications/{applicationId:guid}/approve -> platform-admin",
        "POST /api/v1/platform-admin/organizer-applications/{applicationId:guid}/reject -> platform-admin",
        "PUT /api/v1/competitions/{competitionId:guid}/publication -> competition-owner-write",
        "PUT /api/v1/fantasy/{slug}/lineup/captain -> authenticated-write",
        "PUT /api/v1/fantasy/{slug}/lineup/swap -> authenticated-write",
        "PUT /api/v1/competitions/{competitionId:guid}/matches/{matchId:guid}/sheet/ -> competition-staff-write",
        "PUT /api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid} -> competition-owner-write",
        "PUT /api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid}"
            + "/matches/{matchId:guid} -> competition-owner-write",
        "PUT /api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid}/status -> competition-owner-write",
        "PUT /api/v1/competitions/{competitionId:guid}/settings -> competition-owner-write",
        "PUT /api/v1/competitions/{competitionId:guid}/athletes/{athleteId:guid} -> competition-owner-write",
        "PUT /api/v1/competitions/{competitionId:guid}/coaches/{coachId:guid} -> competition-owner-write",
        "PUT /api/v1/competitions/{competitionId:guid}/stages/order -> competition-owner-write",
        "PUT /api/v1/competitions/{competitionId:guid}/stages/{stageId:guid}/participants -> competition-owner-write",
        "PUT /api/v1/competitions/{competitionId:guid}/stages/{stageId:guid} -> competition-owner-write",
        "PUT /api/v1/competitions/{competitionId:guid}/teams/{teamId:guid} -> competition-owner-write",
    ];

    [Fact]
    public void EveryRouteMatchesTheDocumentedAuthorizationMatrix()
    {
        // O host de teste roda como Development, então o OpenAPI também aparece aqui;
        // em produção ele não é mapeado (OpenApiExposureTests).
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var actual = endpoints.OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => $"{method} {endpoint.RoutePattern.RawText} -> {Requirement(endpoint)}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedMatrix.Order(StringComparer.Ordinal), actual);
    }

    private static string Requirement(Endpoint endpoint)
    {
        var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        return authorize.Count == 0
            ? Anonymous
            : string.Join(",", authorize.Select(item => item.Policy ?? Authenticated));
    }
}
