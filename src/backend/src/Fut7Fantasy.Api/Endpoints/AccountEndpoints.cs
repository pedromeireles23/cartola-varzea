using System.ComponentModel.DataAnnotations;
using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Accounts;
using Fut7Fantasy.Infrastructure.Options;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

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
            .AddEndpointFilter(OnlyWithEmail)
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

        grupo.MapPost("/login/two-factor", LoginWithTwoFactorAsync)
            .RequireRateLimiting(StrictRateLimitPolicy)
            .WithName("LoginWithTwoFactor")
            .WithSummary("Conclui a entrada de uma conta com dois fatores.");

        var duasEtapas = grupo.MapGroup("/two-factor");

        duasEtapas.MapGet("/", TwoFactorStatusAsync)
            .RequireAuthorization()
            .WithName("TwoFactorStatus")
            .WithSummary("Diz se a conta exige o segundo fator e quantos códigos de recuperação restam.");

        duasEtapas.MapPost("/authenticator", BeginAuthenticatorSetupAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("BeginAuthenticatorSetup")
            .WithSummary("Gera a chave do aplicativo autenticador, antes de ligar os dois fatores.");

        duasEtapas.MapPost("/enable", EnableTwoFactorAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .RequireRateLimiting(StrictRateLimitPolicy)
            .WithName("EnableTwoFactor")
            .WithSummary("Liga os dois fatores com um código do aplicativo e devolve os códigos de recuperação.");

        grupo.MapGet("/demo", DemoAvailability)
            .WithName("DemoAvailability")
            .WithSummary("Diz se a entrada de visitante da demonstração está ligada.");

        grupo.MapPost("/demo", DemoSignInAsync)
            .RequireRateLimiting(StrictRateLimitPolicy)
            .WithName("DemoSignIn")
            .WithSummary("Abre a sessão somente leitura da demonstração, sem senha.");

        grupo.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Encerra a sessão.");

        grupo.MapPost("/forgot-password", ForgotPasswordAsync)
            .AddEndpointFilter(OnlyWithEmail)
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

            // Senha certa numa conta com dois fatores: a sessão ainda não existe, e a tela
            // pede o código. Só quem acertou a senha chega a esta resposta.
            SignInOutcome.TwoFactorRequired => Results.Ok(new LoginResponse(TwoFactorRequired: true)),

            SignInOutcome.EmailNotConfirmed => Results.Problem(
                title: "E-mail ainda não confirmado",
                detail: "Confirme seu e-mail pelo link que enviamos antes de entrar.",
                statusCode: StatusCodes.Status403Forbidden),

            // E-mail inexistente, senha errada e conta bloqueada devolvem exatamente
            // a mesma resposta; o texto avisa do bloqueio sem dizer se ele ocorreu.
            _ => Results.Problem(
                title: "E-mail ou senha incorretos",
                detail: "Confira os dados e tente de novo. Depois de várias tentativas seguidas, "
                    + "a entrada fica pausada por alguns minutos.",
                statusCode: StatusCodes.Status401Unauthorized),
        };
    }

    private static async Task<IResult> LoginWithTwoFactorAsync(
        TwoFactorCodeRequest request,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (Validar(request) is { } invalido)
        {
            return invalido;
        }

        // Código errado, conta bloqueada e primeiro passo vencido respondem igual: a tela
        // manda voltar ao começo se insistir.
        return await accounts.SignInWithTwoFactorAsync(request.Code, cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.Problem(
                title: "Código não confere",
                detail: "Confira o código no aplicativo e tente de novo. Depois de cinco minutos, "
                    + "ou de várias tentativas seguidas, é preciso entrar de novo com e-mail e senha.",
                statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> TwoFactorStatusAsync(
        IAccountService accounts,
        CancellationToken cancellationToken) =>
        await accounts.GetTwoFactorStatusAsync(cancellationToken).ConfigureAwait(false) is { } status
            ? Results.Ok(status)
            : Results.Unauthorized();

    private static async Task<IResult> BeginAuthenticatorSetupAsync(
        IAccountService accounts,
        CancellationToken cancellationToken) =>
        await accounts.BeginAuthenticatorSetupAsync(cancellationToken).ConfigureAwait(false) is { } setup
            ? Results.Ok(setup)
            : TwoFactorAlreadyEnabled();

    private static async Task<IResult> EnableTwoFactorAsync(
        TwoFactorCodeRequest request,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (Validar(request) is { } invalido)
        {
            return invalido;
        }

        if (await accounts.GetTwoFactorStatusAsync(cancellationToken).ConfigureAwait(false) is { Enabled: true })
        {
            return TwoFactorAlreadyEnabled();
        }

        return await accounts.EnableTwoFactorAsync(request.Code, cancellationToken).ConfigureAwait(false)
            is { } codigos
            ? Results.Ok(new RecoveryCodesResponse(codigos))
            : Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["code"] = ["O código não confere. Use o que o aplicativo mostra agora."],
                },
                title: "Código não confere");
    }

    private static IResult TwoFactorAlreadyEnabled() =>
        Results.Problem(
            title: "Verificação em duas etapas já ligada",
            detail: "A conta já usa o aplicativo autenticador.",
            statusCode: StatusCodes.Status409Conflict);

    /// <summary>
    /// A tela de entrada pergunta antes de mostrar o botão: fora do ambiente de
    /// demonstração ele não existe. A mesma resposta diz se dá para criar conta e recuperar
    /// a senha, o que a publicação sem e-mail fecha (Fase 17).
    /// </summary>
    private static IResult DemoAvailability(
        IOptions<DemoAccessOptions> demo,
        IOptions<EmailOptions> email) =>
        Results.Ok(new DemoAvailabilityResponse(demo.Value.Enabled, email.Value.Enabled));

    /// <summary>
    /// Cadastro e recuperação de senha só existem por e-mail. Sem ele, respondem como rota
    /// inexistente, do mesmo jeito que a entrada de visitante desligada.
    /// </summary>
    private static async ValueTask<object?> OnlyWithEmail(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next) =>
        context.HttpContext.RequestServices.GetRequiredService<IOptions<EmailOptions>>().Value.Enabled
            ? await next(context).ConfigureAwait(false)
            : Results.NotFound();

    /// <summary>
    /// Entrada de visitante (Fase 12). Desligada, ou com a conta configurada fora do papel
    /// <c>DemoViewer</c>, responde como rota inexistente.
    /// </summary>
    private static async Task<IResult> DemoSignInAsync(
        IAccountService accounts,
        CancellationToken cancellationToken) =>
        await accounts.SignInDemoViewerAsync(cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();

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

/// <param name="TwoFactorRequired">A senha confere, e a sessão espera o segundo fator.</param>
public sealed record LoginResponse(bool TwoFactorRequired);

/// <param name="RecoveryCodes">Os códigos de recuperação, que só saem esta vez.</param>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record TwoFactorCodeRequest(
    [property: Required(ErrorMessage = "Informe o código.")]
    [property: StringLength(32, MinimumLength = 6, ErrorMessage = "Informe o código.")]
    string Code);

/// <param name="Available">A entrada de visitante está ligada.</param>
/// <param name="SelfService">Dá para criar conta e recuperar a senha por e-mail.</param>
public sealed record DemoAvailabilityResponse(bool Available, bool SelfService);

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
