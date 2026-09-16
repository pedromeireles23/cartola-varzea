using System.ComponentModel.DataAnnotations;
using Fut7Fantasy.Application.Accounts;
using Microsoft.AspNetCore.Antiforgery;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Endpoints de conta e sessao.</summary>
public static class AccountEndpoints
{
    /// <summary>Politica de rate limit dos endpoints sensiveis a automacao.</summary>
    public const string StrictRateLimitPolicy = "conta-estrita";

    /// <summary>
    /// Resposta unica de cadastro e de recuperacao.
    ///
    /// E deliberadamente vaga: a mesma mensagem sai quando o e-mail existe e quando
    /// nao existe, para que a API nao vire um oraculo de contas (04-seguranca §5).
    /// </summary>
    private const string CheckYourEmail =
        "Se este e-mail puder ser usado, você vai receber uma mensagem com os próximos passos.";

    /// <summary>Registra as rotas de conta em /api/v1/auth.</summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var grupo = routes.MapGroup("/api/v1/auth").WithTags("Conta");

        grupo.MapPost("/register", RegisterAsync)
            .RequireRateLimiting(StrictRateLimitPolicy)
            .WithName("Register")
            .WithSummary("Cria uma conta e envia a verificação de e-mail.");

        grupo.MapPost("/confirm-email", ConfirmEmailAsync)
            .RequireRateLimiting(StrictRateLimitPolicy)
            .WithName("ConfirmEmail")
            .WithSummary("Confirma o e-mail a partir do token recebido por mensagem.");

        grupo.MapPost("/login", LoginAsync)
            .RequireRateLimiting(StrictRateLimitPolicy)
            .WithName("Login")
            .WithSummary("Abre a sessão em cookie.");

        grupo.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Encerra a sessão.");

        grupo.MapPost("/forgot-password", ForgotPasswordAsync)
            .RequireRateLimiting(StrictRateLimitPolicy)
            .WithName("ForgotPassword")
            .WithSummary("Solicita a recuperação de senha.");

        grupo.MapPost("/reset-password", ResetPasswordAsync)
            .RequireRateLimiting(StrictRateLimitPolicy)
            .WithName("ResetPassword")
            .WithSummary("Conclui a recuperação e invalida as sessões anteriores.");

        grupo.MapGet("/me", MeAsync)
            .WithName("Me")
            .WithSummary("Dados da conta autenticada.");

        grupo.MapGet("/antiforgery", IssueAntiforgeryToken)
            .WithName("Antiforgery")
            .WithSummary("Emite o par de tokens antiforgery para o SPA.");

        return routes;
    }

    /// <summary>
    /// Publica o request token num cookie legivel por script, que e como o
    /// HttpClient do Angular o encontra. O cookie do antiforgery em si continua
    /// HttpOnly: os dois juntos e que formam a protecao.
    /// </summary>
    private static IResult IssueAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);

        context.Response.Cookies.Append(
            "XSRF-TOKEN",
            tokens.RequestToken!,
            new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Path = "/",
            });

        return Results.NoContent();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (Validar(request) is { } invalido)
        {
            return invalido;
        }

        var resultado = await accounts
            .RegisterAsync(request.Email, request.DisplayName, request.Password, cancellationToken)
            .ConfigureAwait(false);

        // Senha fraca e o unico caso que volta como erro: recusar sem dizer o motivo
        // deixaria a pessoa sem saber o que corrigir, e nao revela nada sobre contas.
        if (resultado.Outcome == RegistrationOutcome.PasswordRejected)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["password"] = [.. resultado.Errors] },
                title: "Senha recusada");
        }

        // Created e EmailAlreadyRegistered respondem igual, de propósito.
        return Results.Accepted(value: new MessageResponse(CheckYourEmail));
    }

    private static async Task<IResult> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (Validar(request) is { } invalido)
        {
            return invalido;
        }

        var confirmado = await accounts
            .ConfirmEmailAsync(request.UserId, request.Token, cancellationToken)
            .ConfigureAwait(false);

        return confirmado
            ? Results.Ok(new MessageResponse("E-mail confirmado. Você já pode entrar."))
            : Results.Problem(
                title: "Link inválido ou expirado",
                detail: "Peça um novo link de confirmação na tela de entrada.",
                statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (Validar(request) is { } invalido)
        {
            return invalido;
        }

        var resultado = await accounts
            .SignInAsync(request.Email, request.Password, cancellationToken)
            .ConfigureAwait(false);

        return resultado switch
        {
            SignInOutcome.Success => Results.NoContent(),

            SignInOutcome.EmailNotConfirmed => Results.Problem(
                title: "E-mail ainda não confirmado",
                detail: "Confirme seu e-mail pelo link que enviamos antes de entrar.",
                statusCode: StatusCodes.Status403Forbidden),

            SignInOutcome.LockedOut => Results.Problem(
                title: "Muitas tentativas",
                detail: "Aguarde alguns minutos antes de tentar de novo.",
                statusCode: StatusCodes.Status423Locked),

            // E-mail inexistente e senha errada devolvem exatamente a mesma resposta.
            _ => Results.Problem(
                title: "E-mail ou senha incorretos",
                detail: "Confira os dados e tente de novo.",
                statusCode: StatusCodes.Status401Unauthorized),
        };
    }

    private static async Task<IResult> LogoutAsync(
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        await accounts.SignOutAsync(cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (Validar(request) is { } invalido)
        {
            return invalido;
        }

        await accounts.RequestPasswordResetAsync(request.Email, cancellationToken).ConfigureAwait(false);

        // Sempre a mesma resposta, exista ou não a conta.
        return Results.Accepted(value: new MessageResponse(CheckYourEmail));
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (Validar(request) is { } invalido)
        {
            return invalido;
        }

        var redefinida = await accounts
            .ResetPasswordAsync(request.UserId, request.Token, request.NewPassword, cancellationToken)
            .ConfigureAwait(false);

        return redefinida
            ? Results.Ok(new MessageResponse("Senha redefinida. Entre com a senha nova."))
            : Results.Problem(
                title: "Link inválido ou expirado",
                detail: "Peça uma nova recuperação de senha.",
                statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> MeAsync(
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        var perfil = await accounts.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        // Anonimo nao e erro: a interface pergunta sempre, para saber qual casca montar.
        return perfil is null ? Results.NoContent() : Results.Ok(perfil);
    }

    private static IResult? Validar<T>(T request)
        where T : notnull
    {
        var contexto = new ValidationContext(request);
        var erros = new List<ValidationResult>();

        if (Validator.TryValidateObject(request, contexto, erros, validateAllProperties: true))
        {
            return null;
        }

        var porCampo = erros
            .SelectMany(erro => erro.MemberNames.DefaultIfEmpty(string.Empty),
                (erro, campo) => (Campo: campo, erro.ErrorMessage))
            .GroupBy(item => item.Campo, StringComparer.Ordinal)
            .ToDictionary(
                grupo => grupo.Key,
                grupo => grupo.Select(item => item.ErrorMessage ?? "Valor inválido.").ToArray(),
                StringComparer.Ordinal);

        return Results.ValidationProblem(porCampo, title: "Dados inválidos");
    }
}

/// <param name="Message">Texto pronto para exibir ao usuário.</param>
public sealed record MessageResponse(string Message);

public sealed record RegisterRequest(
    [property: Required(ErrorMessage = "Informe o e-mail.")]
    [property: EmailAddress(ErrorMessage = "E-mail inválido.")]
    [property: StringLength(254)]
    string Email,
    [property: Required(ErrorMessage = "Informe como quer ser chamado.")]
    [property: StringLength(80, MinimumLength = 2)]
    string DisplayName,
    [property: Required(ErrorMessage = "Informe a senha.")]
    [property: StringLength(256, MinimumLength = 12, ErrorMessage = "A senha precisa de ao menos 12 caracteres.")]
    string Password);

public sealed record LoginRequest(
    [property: Required][property: EmailAddress][property: StringLength(254)] string Email,
    [property: Required][property: StringLength(256)] string Password);

public sealed record ConfirmEmailRequest(
    [property: Required] Guid UserId,
    [property: Required][property: StringLength(2048)] string Token);

public sealed record ForgotPasswordRequest(
    [property: Required][property: EmailAddress][property: StringLength(254)] string Email);

public sealed record ResetPasswordRequest(
    [property: Required] Guid UserId,
    [property: Required][property: StringLength(2048)] string Token,
    [property: Required]
    [property: StringLength(256, MinimumLength = 12, ErrorMessage = "A senha precisa de ao menos 12 caracteres.")]
    string NewPassword);
