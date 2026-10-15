namespace Fut7Fantasy.Application.Accounts;

/// <summary>
/// Porta das operacoes de conta. A implementacao usa ASP.NET Core Identity; nenhum
/// caso de uso ou endpoint deve falar com UserManager diretamente.
/// </summary>
public interface IAccountService
{
    /// <summary>Cria a conta e dispara a verificacao de e-mail.</summary>
    Task<RegistrationResult> RegisterAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken);

    /// <summary>Confirma o e-mail a partir do token enviado por mensagem.</summary>
    Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken);

    /// <summary>Autentica e abre a sessao em cookie.</summary>
    Task<SignInOutcome> SignInAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Segundo passo da entrada de uma conta com dois fatores: o codigo do aplicativo
    /// autenticador ou um codigo de recuperacao, que deixa de valer depois de usado.
    /// Falso quando o codigo nao confere, quando a conta esta bloqueada ou quando o
    /// primeiro passo nao aconteceu ou ja venceu.
    /// </summary>
    Task<bool> SignInWithTwoFactorAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Abre a sessao da conta publica de demonstracao, sem senha. Falso quando a entrada
    /// de visitante esta desligada ou a conta configurada nao e <c>DemoViewer</c>.
    /// </summary>
    Task<bool> SignInDemoViewerAsync(CancellationToken cancellationToken);

    /// <summary>Encerra a sessao.</summary>
    Task SignOutAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Inicia a recuperacao de senha. Nao informa se o e-mail existe: a resposta e
    /// sempre a mesma e o e-mail so sai quando a conta existe.
    /// </summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);

    /// <summary>Conclui a recuperacao e invalida as sessoes existentes.</summary>
    Task<bool> ResetPasswordAsync(
        Guid userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken);

    /// <summary>Dados da conta autenticada, ou <c>null</c> quando anonima.</summary>
    Task<AccountProfile?> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>Estado dos dois fatores da conta autenticada, ou <c>null</c> quando anonima.</summary>
    Task<TwoFactorStatus?> GetTwoFactorStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gera uma chave nova de aplicativo autenticador para a conta autenticada. Nulo quando
    /// os dois fatores ja estao ligados: trocar a chave ai invalidaria o aplicativo dela.
    /// </summary>
    Task<AuthenticatorSetup?> BeginAuthenticatorSetupAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Liga os dois fatores se o codigo confere com a chave gerada, e devolve os codigos de
    /// recuperacao — a unica vez que eles saem. Nulo quando o codigo nao confere ou quando
    /// ja estavam ligados. A sessao atual continua, agora como sessao de dois fatores.
    /// </summary>
    Task<IReadOnlyList<string>?> EnableTwoFactorAsync(string code, CancellationToken cancellationToken);
}
