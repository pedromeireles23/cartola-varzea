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
}
