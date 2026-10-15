namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>
/// Como a sessão foi aberta. O Identity marca com <c>amr=mfa</c> a sessão que passou pelo
/// segundo fator, e a administração da plataforma exige essa marca (04-seguranca §5).
/// </summary>
public static class SessionClaims
{
    /// <summary>Tipo da claim de método de autenticação (RFC 8176).</summary>
    public const string AuthenticationMethod = "amr";

    /// <summary>Valor que o Identity grava quando a entrada passou pelo segundo fator.</summary>
    public const string MultiFactor = "mfa";
}
