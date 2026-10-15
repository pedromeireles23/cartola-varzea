namespace Fut7Fantasy.Application.Accounts;

/// <summary>Resultado do cadastro, do ponto de vista de quem chama.</summary>
public enum RegistrationOutcome
{
    /// <summary>Conta criada; e-mail de verificacao enviado.</summary>
    Created,

    /// <summary>
    /// O e-mail ja pertence a uma conta. A API responde igual ao caso de sucesso:
    /// quem chama nao pode distinguir os dois (04-seguranca §5).
    /// </summary>
    EmailAlreadyRegistered,

    /// <summary>A senha nao atende a politica. Os motivos vao em <c>Errors</c>.</summary>
    PasswordRejected,
}

/// <param name="Outcome">O que aconteceu.</param>
/// <param name="Errors">Mensagens em pt-BR quando a senha foi recusada.</param>
public sealed record RegistrationResult(RegistrationOutcome Outcome, IReadOnlyList<string> Errors)
{
    public static RegistrationResult Created { get; } = new(RegistrationOutcome.Created, []);

    public static RegistrationResult EmailAlreadyRegistered { get; } =
        new(RegistrationOutcome.EmailAlreadyRegistered, []);

    public static RegistrationResult PasswordRejected(IReadOnlyList<string> errors) =>
        new(RegistrationOutcome.PasswordRejected, errors);
}

/// <summary>Resultado da tentativa de entrada.</summary>
public enum SignInOutcome
{
    Success,

    /// <summary>
    /// E-mail inexistente, senha errada ou conta bloqueada por tentativas
    /// repetidas. Os tres casos sao indistinguiveis.
    /// </summary>
    InvalidCredentials,

    /// <summary>Conta existe e a senha confere, mas o e-mail ainda nao foi verificado.</summary>
    EmailNotConfirmed,

    /// <summary>
    /// A senha confere numa conta com dois fatores: a sessao so abre depois do codigo do
    /// aplicativo autenticador ou de um codigo de recuperacao.
    /// </summary>
    TwoFactorRequired,
}

/// <summary>Dados da conta que a propria pessoa pode ver.</summary>
/// <param name="Id">Identificador externo da conta.</param>
/// <param name="Email">E-mail de acesso.</param>
/// <param name="DisplayName">Nome exibido na interface.</param>
/// <param name="EmailConfirmed">Se o e-mail ja foi verificado.</param>
/// <param name="Roles">Papeis globais da plataforma.</param>
/// <param name="TwoFactorEnabled">
/// Se a conta exige o segundo fator. Ligado, toda sessao dela passou pelo codigo: ligar
/// troca o security stamp, o que derruba as sessoes abertas so com senha.
/// </param>
public sealed record AccountProfile(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    bool TwoFactorEnabled);

/// <param name="Enabled">Se a conta exige o segundo fator.</param>
/// <param name="RecoveryCodesLeft">Codigos de recuperacao ainda nao usados.</param>
public sealed record TwoFactorStatus(bool Enabled, int RecoveryCodesLeft);

/// <summary>O que a pessoa leva para o aplicativo autenticador.</summary>
/// <param name="SharedKey">A chave em grupos de quatro, para digitar.</param>
/// <param name="AuthenticatorUri">O endereco <c>otpauth://</c>, conteudo do QR code.</param>
public sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);
