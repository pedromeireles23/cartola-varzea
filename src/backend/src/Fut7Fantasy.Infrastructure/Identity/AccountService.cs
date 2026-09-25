using System.Security.Cryptography;
using System.Text;
using System.Web;
using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Accounts;
using Fut7Fantasy.Infrastructure.Options;
using Fut7Fantasy.Infrastructure.PlatformAdministration;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>Implementacao de <see cref="IAccountService"/> sobre ASP.NET Core Identity.</summary>
public sealed class AccountService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IEmailSender emailSender,
    ICurrentUser currentUser,
    InitialPlatformAdmin initialAdmin,
    TimeProvider clock,
    IOptions<AuthenticationOptions> authOptions,
    IOptions<DemoAccessOptions> demoAccess,
    ILogger<AccountService> logger) : IAccountService
{
    /// <summary>Dono do hash de referencia; nunca e persistido.</summary>
    private static readonly ApplicationUser ContaDeReferencia = new()
    {
        DisplayName = string.Empty,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static string? _hashDeReferencia;

    private readonly AuthenticationOptions _auth = authOptions.Value;

    /// <inheritdoc />
    public async Task<RegistrationResult> RegisterAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        // E-mail duplicado nao vira erro: quem chama recebe a mesma resposta do
        // sucesso, e quem ja tem conta recebe um aviso por e-mail. Sem isso, o
        // cadastro vira um oraculo de quais enderecos existem na plataforma.
        var existente = await users.FindByEmailAsync(email).ConfigureAwait(false);
        if (existente is not null)
        {
            var mascarado = SecurityEvents.Mask(email);
            SecurityEvents.RegistrationRejectedDuplicate(logger, mascarado);
            await EnviarAvisoDeDuplicidadeAsync(email, cancellationToken).ConfigureAwait(false);
            return RegistrationResult.EmailAlreadyRegistered;
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = displayName,
            CreatedAt = clock.GetUtcNow(),
        };

        var resultado = await users.CreateAsync(user, password).ConfigureAwait(false);
        if (!resultado.Succeeded)
        {
            // Erro de senha e o unico que pode voltar para a interface; o caso de
            // e-mail duplicado ja foi tratado acima sem revelar nada.
            var mensagens = resultado.Errors.Select(erro => erro.Description).ToArray();
            return RegistrationResult.PasswordRejected(mensagens);
        }

        SecurityEvents.AccountCreated(logger, user.Id);
        await EnviarVerificacaoAsync(user, cancellationToken).ConfigureAwait(false);
        return RegistrationResult.Created;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            SecurityEvents.InvalidToken(logger, "confirmacao de e-mail");
            return false;
        }

        // Confirmar de novo com o mesmo link nao pode parecer uma operacao valida.
        // O provedor padrao do Identity pode continuar aceitando o token enquanto
        // o security stamp nao muda, entao o estado da conta fecha o uso unico.
        if (await users.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            SecurityEvents.InvalidToken(logger, "confirmacao de e-mail");
            return false;
        }

        var resultado = await users.ConfirmEmailAsync(user, Decode(token)).ConfigureAwait(false);
        if (!resultado.Succeeded)
        {
            SecurityEvents.InvalidToken(logger, "confirmacao de e-mail");
            return false;
        }

        SecurityEvents.EmailConfirmed(logger, user.Id);
        await ConcederAdministradorInicialAsync(user, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<SignInOutcome> SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var mascarado = SecurityEvents.Mask(email);
        var user = await users.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null)
        {
            // Confere a senha contra um hash qualquer para gastar o mesmo tempo de
            // uma conta real; sem isso, a demora da resposta revelaria quem existe.
            users.PasswordHasher.VerifyHashedPassword(ContaDeReferencia, HashDeReferencia(), password);
            SecurityEvents.SignInFailed(logger, mascarado);
            return SignInOutcome.InvalidCredentials;
        }

        // Nao usa PasswordSignInAsync: ele recusa conta bloqueada ou sem e-mail
        // confirmado antes de olhar a senha, e as duas respostas diferentes
        // confirmariam, com qualquer senha, que o e-mail tem conta.
        var senhaConfere = await users.CheckPasswordAsync(user, password).ConfigureAwait(false);

        // Bloqueada responde como senha errada, mesmo com a senha certa: se a
        // resposta mudasse, o bloqueio confirmaria o palpite em vez de barra-lo.
        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            SecurityEvents.SignInLockedOut(logger, mascarado);
            return SignInOutcome.InvalidCredentials;
        }

        if (!senhaConfere)
        {
            await users.AccessFailedAsync(user).ConfigureAwait(false);
            SecurityEvents.SignInFailed(logger, mascarado);
            return SignInOutcome.InvalidCredentials;
        }

        // Daqui em diante quem pergunta ja provou conhecer a senha.
        if (!await signIn.CanSignInAsync(user).ConfigureAwait(false))
        {
            return SignInOutcome.EmailNotConfirmed;
        }

        await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        await signIn.SignInAsync(user, isPersistent: true).ConfigureAwait(false);
        SecurityEvents.SignInSucceeded(logger, user.Id);
        return SignInOutcome.Success;
    }

    /// <inheritdoc />
    public async Task<bool> SignInDemoViewerAsync(CancellationToken cancellationToken)
    {
        var options = demoAccess.Value;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ViewerEmail))
        {
            return false;
        }

        // Só a conta de demonstração: uma configuração errada apontando para outra conta
        // não pode virar uma porta sem senha para ela.
        var user = await users.FindByEmailAsync(options.ViewerEmail).ConfigureAwait(false);
        if (user is null
            || !await users.IsInRoleAsync(user, ApplicationRole.DemoViewer).ConfigureAwait(false)
            || !await signIn.CanSignInAsync(user).ConfigureAwait(false))
        {
            return false;
        }

        // Sessão só do navegador aberto: em computador compartilhado ela não fica.
        await signIn.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
        SecurityEvents.DemoSignIn(logger, user.Id);
        return true;
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var id = currentUser.Id;
        await signIn.SignOutAsync().ConfigureAwait(false);

        if (id is not null)
        {
            SecurityEvents.SignedOut(logger, id.Value);
        }
    }

    /// <inheritdoc />
    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var mascarado = SecurityEvents.Mask(email);
        SecurityEvents.PasswordResetRequested(logger, mascarado);

        var user = await users.FindByEmailAsync(email).ConfigureAwait(false);

        // Conta inexistente ou e-mail nao verificado: nada e enviado e a resposta da
        // API continua identica. Enviar aqui confirmaria a existencia do endereco.
        if (user is null || !await users.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        var link = MontarLink("/recuperar-senha", user.Id, token);
        var (assunto, html, texto) = AccountEmails.PasswordReset(user.DisplayName, link);

        await EnviarAsync(user.Email!, assunto, html, texto, "recuperacao de senha", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ResetPasswordAsync(
        Guid userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            SecurityEvents.InvalidToken(logger, "redefinicao de senha");
            return false;
        }

        var resultado = await users.ResetPasswordAsync(user, Decode(token), newPassword).ConfigureAwait(false);
        if (!resultado.Succeeded)
        {
            SecurityEvents.InvalidToken(logger, "redefinicao de senha");
            return false;
        }

        // Invalida as sessoes abertas: o cookie carrega o security stamp, e troca-lo
        // faz qualquer sessao anterior deixar de validar (04-seguranca §5).
        await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
        SecurityEvents.PasswordReset(logger, user.Id);
        return true;
    }

    /// <inheritdoc />
    public async Task<AccountProfile?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } id)
        {
            return null;
        }

        var user = await users.FindByIdAsync(id.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        var papeis = await users.GetRolesAsync(user).ConfigureAwait(false);
        return new AccountProfile(user.Id, user.Email!, user.DisplayName, user.EmailConfirmed, [.. papeis]);
    }

    /// <summary>
    /// A confirmação já valeu; uma falha ao conceder o administrador inicial não pode
    /// desfazê-la. A concessão é repetida no próximo início da aplicação.
    /// </summary>
    private async Task ConcederAdministradorInicialAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        try
        {
            await initialAdmin.GrantIfConfiguredAsync(user, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Falha aqui vira log; a conta confirmada continua valendo.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            PlatformAdministrationEvents.InitialAdminGrantFailed(logger, exception);
        }
    }

    private async Task EnviarVerificacaoAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user).ConfigureAwait(false);
        var link = MontarLink("/verificar-email", user.Id, token);
        var (assunto, html, texto) = AccountEmails.Verification(user.DisplayName, link);

        await EnviarAsync(user.Email!, assunto, html, texto, "verificacao de e-mail", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnviarAvisoDeDuplicidadeAsync(string destino, CancellationToken cancellationToken)
    {
        var link = new Uri(new Uri(_auth.PublicOrigin), "/entrar");
        var (assunto, html, texto) = AccountEmails.DuplicateRegistration(link);

        await EnviarAsync(destino, assunto, html, texto, "cadastro duplicado", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Falha de e-mail e registrada e engolida: a conta ja foi criada ou a senha ja
    /// foi redefinida, e desfazer isso deixaria o estado pior (04-seguranca §14).
    /// </summary>
    private async Task EnviarAsync(
        string destino,
        string assunto,
        string html,
        string texto,
        string proposito,
        CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(destino, assunto, html, texto, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Qualquer falha de SMTP vira log, nunca erro para o usuario.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            SecurityEvents.EmailSendFailed(logger, proposito, exception);
        }
    }

    /// <summary>
    /// O token do Identity nao e seguro para URL; vai em base64url e o ID fica na
    /// query, nunca no fragmento, para que o backend consiga valida-lo.
    /// </summary>
    private Uri MontarLink(string caminho, Guid userId, string token)
    {
        var codificado = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var query = $"?id={userId}&token={HttpUtility.UrlEncode(codificado)}";

        return new Uri(new Uri(_auth.PublicOrigin), caminho + query);
    }

    /// <summary>
    /// Hash de uma senha aleatoria, gerado pelo mesmo hasher e com os mesmos
    /// parametros das contas reais. Calcular duas vezes numa corrida e inofensivo.
    /// </summary>
    private string HashDeReferencia() =>
        _hashDeReferencia ??= users.PasswordHasher.HashPassword(
            ContaDeReferencia, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    private static string Decode(string token)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            // Token adulterado: devolve algo que o Identity vai recusar.
            return string.Empty;
        }
    }
}
