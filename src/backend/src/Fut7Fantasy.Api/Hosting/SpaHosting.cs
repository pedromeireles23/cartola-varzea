using System.Text.RegularExpressions;

namespace Fut7Fantasy.Api.Hosting;

/// <summary>
/// O build do Angular servido pela própria API, na mesma origem (03 §15): o cookie de
/// sessão, o antiforgery e a CSP <c>'self'</c> contam com isso, e não existe CORS. A
/// publicação copia o build para o <c>wwwroot</c>; em desenvolvimento ele não existe, e
/// quem serve o SPA é o <c>ng serve</c>.
/// </summary>
public static partial class SpaHosting
{
    /// <summary>
    /// Os pacotes que o Angular nomeia com hash do conteúdo. Mudou o conteúdo, mudou o nome,
    /// então podem ficar no cache para sempre; o resto — o <c>index.html</c> sobretudo, que
    /// aponta para os pacotes da versão atual — é revalidado a cada visita.
    /// </summary>
    [GeneratedRegex(@"^(main|chunk|polyfills|styles)-[A-Za-z0-9_-]{8}\.(js|css)$")]
    private static partial Regex HashedBundle();

    /// <summary>Cabeçalhos de cache dos arquivos do build.</summary>
    public static IServiceCollection AddSpaHosting(this IServiceCollection services) =>
        services.Configure<StaticFileOptions>(options => options.OnPrepareResponse = context =>
            context.Context.Response.Headers.CacheControl = HashedBundle().IsMatch(context.File.Name)
                ? "public, max-age=31536000, immutable"
                : "no-cache");

    /// <summary>
    /// Serve o build e as rotas do SPA. Um GET ou HEAD que não casou com nenhum endpoint,
    /// fora de <c>/api</c> e sem extensão, é uma tela do Angular e recebe o
    /// <c>index.html</c>; o resto segue como antes.
    ///
    /// Não é um endpoint de fallback de propósito: ele concorreria com as rotas da API, e
    /// um GET numa rota que só aceita POST passaria de 405 a 404. Precisa vir depois do
    /// roteamento, que já escolheu o endpoint, e antes da autenticação, porque arquivo
    /// estático não depende de quem pede.
    /// </summary>
    public static IApplicationBuilder UseSpa(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use((context, next) =>
        {
            var request = context.Request;
            if (context.GetEndpoint() is null
                && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                && !request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                && !Path.HasExtension(request.Path.Value))
            {
                request.Path = "/index.html";
            }

            return next(context);
        });

        return app.UseStaticFiles();
    }
}
