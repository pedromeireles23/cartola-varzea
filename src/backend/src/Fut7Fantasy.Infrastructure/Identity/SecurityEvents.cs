using Microsoft.Extensions.Logging;

namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>
/// Eventos de seguranca de conta (04-seguranca §11).
///
/// Nenhuma mensagem carrega senha, token, cookie ou o e-mail completo: o
/// identificador e o ID da conta, e o e-mail aparece apenas mascarado quando nao
/// existe conta para referenciar.
/// </summary>
public static partial class SecurityEvents
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Conta criada. userId={UserId}")]
    public static partial void AccountCreated(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Cadastro recusado: e-mail já registrado. email={MaskedEmail}")]
    public static partial void RegistrationRejectedDuplicate(ILogger logger, string maskedEmail);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information,
        Message = "E-mail confirmado. userId={UserId}")]
    public static partial void EmailConfirmed(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information,
        Message = "Entrada bem-sucedida. userId={UserId}")]
    public static partial void SignInSucceeded(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning,
        Message = "Credenciais inválidas. email={MaskedEmail}")]
    public static partial void SignInFailed(ILogger logger, string maskedEmail);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning,
        Message = "Conta bloqueada por tentativas repetidas. email={MaskedEmail}")]
    public static partial void SignInLockedOut(ILogger logger, string maskedEmail);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information,
        Message = "Sessão encerrada. userId={UserId}")]
    public static partial void SignedOut(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Information,
        Message = "Recuperação de senha solicitada. email={MaskedEmail}")]
    public static partial void PasswordResetRequested(ILogger logger, string maskedEmail);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Information,
        Message = "Senha redefinida; sessões anteriores invalidadas. userId={UserId}")]
    public static partial void PasswordReset(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Warning,
        Message = "Token de conta inválido ou expirado. propósito={Purpose}")]
    public static partial void InvalidToken(ILogger logger, string purpose);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Error,
        Message = "Falha ao enviar e-mail de conta. propósito={Purpose}")]
    public static partial void EmailSendFailed(ILogger logger, string purpose, Exception exception);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Information,
        Message = "Sessão de demonstração aberta sem senha. userId={UserId}")]
    public static partial void DemoSignIn(ILogger logger, Guid userId);

    /// <summary>
    /// Reduz o e-mail ao minimo util para investigar abuso: primeira letra, dominio
    /// e nada mais. Registrar o endereco inteiro seria dado pessoal desnecessario.
    /// </summary>
    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "(vazio)";
        }

        var partes = email.Split('@', 2);
        if (partes.Length != 2 || partes[0].Length == 0)
        {
            return "(inválido)";
        }

        return $"{partes[0][0]}***@{partes[1]}";
    }
}
