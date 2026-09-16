using System.Globalization;
using System.Net;

namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>
/// Textos dos e-mails de conta. Todo valor vindo do usuario e escapado antes de
/// entrar no HTML; o nome exibido e conteudo controlado por quem se cadastrou.
/// </summary>
internal static class AccountEmails
{
    public static (string Subject, string Html, string Text) Verification(string displayName, Uri link)
    {
        var nome = WebUtility.HtmlEncode(displayName);
        var href = WebUtility.HtmlEncode(link.ToString());

        var html = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            <p>Olá, {nome}.</p>
            <p>Confirme seu e-mail para começar a jogar:</p>
            <p><a href="{href}">Confirmar meu e-mail</a></p>
            <p>O link vale por 24 horas. Se não foi você que criou a conta, ignore esta mensagem.</p>
            """);

        var texto = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            Olá, {displayName}.

            Confirme seu e-mail para começar a jogar:
            {link}

            O link vale por 24 horas. Se não foi você que criou a conta, ignore esta mensagem.
            """);

        return ("Confirme seu e-mail", html, texto);
    }

    /// <summary>
    /// Enviado quando alguem tenta cadastrar um e-mail que ja tem conta. E o que
    /// permite responder igual nos dois casos sem esconder a tentativa do titular.
    /// </summary>
    public static (string Subject, string Html, string Text) DuplicateRegistration(Uri signInLink)
    {
        var href = WebUtility.HtmlEncode(signInLink.ToString());

        var html = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            <p>Alguém tentou criar uma conta com este e-mail, mas você já tem uma.</p>
            <p><a href="{href}">Entrar na minha conta</a></p>
            <p>Se esqueceu a senha, use a opção de recuperação na tela de entrada.</p>
            <p>Se não foi você, não precisa fazer nada: nenhuma conta nova foi criada.</p>
            """);

        var texto = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            Alguém tentou criar uma conta com este e-mail, mas você já tem uma.

            Entrar: {signInLink}

            Se esqueceu a senha, use a opção de recuperação na tela de entrada.
            Se não foi você, não precisa fazer nada: nenhuma conta nova foi criada.
            """);

        return ("Já existe uma conta com este e-mail", html, texto);
    }

    public static (string Subject, string Html, string Text) PasswordReset(string displayName, Uri link)
    {
        var nome = WebUtility.HtmlEncode(displayName);
        var href = WebUtility.HtmlEncode(link.ToString());

        var html = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            <p>Olá, {nome}.</p>
            <p>Recebemos um pedido para redefinir sua senha:</p>
            <p><a href="{href}">Criar uma senha nova</a></p>
            <p>O link vale por 1 hora e só pode ser usado uma vez.</p>
            <p>Se não foi você, ignore esta mensagem: sua senha atual continua valendo.</p>
            """);

        var texto = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            Olá, {displayName}.

            Recebemos um pedido para redefinir sua senha:
            {link}

            O link vale por 1 hora e só pode ser usado uma vez.
            Se não foi você, ignore esta mensagem: sua senha atual continua valendo.
            """);

        return ("Redefinir sua senha", html, texto);
    }
}
