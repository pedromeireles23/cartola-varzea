using Microsoft.AspNetCore.Antiforgery;

namespace Fut7Fantasy.Api.Security;

/// <summary>Middlewares de segurança da borda HTTP.</summary>
public static class SecurityMiddleware
{
    private static readonly string[] MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

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

            await Results.Problem(
                    title: "Modo demonstração",
                    detail: "Esta conta é somente leitura.",
                    statusCode: StatusCodes.Status403Forbidden)
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
