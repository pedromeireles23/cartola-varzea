using Microsoft.AspNetCore.Antiforgery;

namespace Fut7Fantasy.Api.Security;

/// <summary>Middlewares de segurança da borda HTTP.</summary>
public static class SecurityMiddleware
{
    /// <summary>Código de erro devolvido quando a conta de demonstração tenta escrever.</summary>
    public const string DemoReadOnlyCode = "demo_read_only";

    /// <summary>Onde mora a API da administração da plataforma.</summary>
    public const string PlatformAdminPath = "/api/v1/platform-admin";

    private static readonly string[] MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>
    /// A administração não existe para quem não é Platform admin: em vez de 401 ou 403, que
    /// confirmariam a rota, a requisição segue sem endpoint, e o resto do pipeline a trata
    /// como qualquer caminho desconhecido — o mesmo 404, o mesmo antiforgery antes dele, o
    /// mesmo bloqueio do visitante e nenhum 405 por método errado. A policy de cada endpoint
    /// continua valendo para quem passa daqui.
    /// </summary>
    public static IApplicationBuilder UseConcealedPlatformAdministration(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(PlatformAdminPath, StringComparison.OrdinalIgnoreCase)
                && !context.User.IsInRole(Fut7Fantasy.Infrastructure.Identity.ApplicationRole.PlatformAdmin))
            {
                context.SetEndpoint(null);
            }

            return next(context);
        });
    }

    /// <summary>Bloqueia mutações da conta pública de demonstração no servidor.</summary>
    public static IApplicationBuilder UseDemoViewerReadOnly(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var isMutation = MutatingMethods.Contains(
                context.Request.Method, StringComparer.OrdinalIgnoreCase);
            var isLogout = context.Request.Path == "/api/v1/auth/logout";
            if (!isMutation
                || isLogout
                || !context.User.IsInRole(Fut7Fantasy.Infrastructure.Identity.ApplicationRole.DemoViewer))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            // O código estável deixa a interface explicar o bloqueio sem depender do título.
            await Results.Problem(
                    title: "Modo demonstração",
                    detail: "Esta conta é somente leitura.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?> { ["code"] = DemoReadOnlyCode })
                .ExecuteAsync(context)
                .ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Exige antiforgery em toda requisição que altera estado e vinda de cookie.
    ///
    /// Aplicar como middleware, e não endpoint a endpoint, é deliberado: a falha
    /// perigosa é esquecer de marcar um endpoint novo, e aqui o padrão é seguro.
    /// GET nunca altera estado, então fica de fora.
    /// </summary>
    public static IApplicationBuilder UseAntiforgeryForMutations(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var metodo = context.Request.Method;
            if (!MutatingMethods.Contains(metodo, StringComparer.OrdinalIgnoreCase))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();

            try
            {
                await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            }
            catch (AntiforgeryValidationException)
            {
                await Results
                    .Problem(
                        title: "Requisição recusada",
                        detail: "Atualize a página e tente de novo.",
                        statusCode: StatusCodes.Status400BadRequest)
                    .ExecuteAsync(context)
                    .ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Cabeçalhos de resposta do 04-seguranca §7. A CSP começa restritiva porque a
    /// aplicação não usa script externo nem inline; ajustar depois é mais seguro do
    /// que começar permissivo e tentar apertar.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-Frame-Options"] = "DENY";
            headers["Content-Security-Policy"] =
                "default-src 'self'; "
                + "base-uri 'self'; "
                + "form-action 'self'; "
                + "frame-ancestors 'none'; "
                + "object-src 'none'; "
                + "img-src 'self' data:; "
                + "style-src 'self' 'unsafe-inline'; "
                + "script-src 'self'";

            await next(context).ConfigureAwait(false);
        });
    }
}
