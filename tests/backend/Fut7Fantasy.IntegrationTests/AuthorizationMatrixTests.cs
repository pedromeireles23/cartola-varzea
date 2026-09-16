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
        "DELETE /api/v1/organizations/{organizationId:guid}/team/assistants/{userId:guid} -> organization-owner-write",
        "DELETE /api/v1/organizations/{organizationId:guid}/team/invitations/{invitationId:guid}"
            + " -> organization-owner-write",
        "GET /api/v1/auth/antiforgery -> " + Anonymous,
        "GET /api/v1/auth/me -> " + Anonymous,
        "GET /api/v1/organizations/mine -> " + Authenticated,
        "GET /api/v1/organizations/{organizationId:guid} -> organization-member",
        "GET /api/v1/organizations/{organizationId:guid}/team/ -> organization-owner",
        "GET /api/v1/organizer-applications/mine -> " + Authenticated,
        "GET /api/v1/platform-admin/organizer-applications/pending -> platform-admin",
        "GET /api/v1/system/info -> " + Anonymous,
        "GET /openapi/{documentName}.json -> " + Anonymous,
        "POST /api/v1/auth/confirm-email -> " + Anonymous,
        "POST /api/v1/auth/forgot-password -> " + Anonymous,
        "POST /api/v1/auth/login -> " + Anonymous,
        "POST /api/v1/auth/logout -> " + Authenticated,
        "POST /api/v1/auth/register -> " + Anonymous,
        "POST /api/v1/auth/reset-password -> " + Anonymous,
        "POST /api/v1/organization-invitations/accept -> authenticated-write",
        "POST /api/v1/organizations/{organizationId:guid}/team/invitations -> organization-owner-write",
        "POST /api/v1/organizer-applications/ -> " + Authenticated,
        "POST /api/v1/platform-admin/organizer-applications/{applicationId:guid}/approve -> platform-admin",
        "POST /api/v1/platform-admin/organizer-applications/{applicationId:guid}/reject -> platform-admin",
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
