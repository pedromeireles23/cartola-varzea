using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Fluxo de conta contra SQL Server real: cadastro, verificacao, entrada,
/// recuperacao e as protecoes que os acompanham.
/// </summary>
public sealed partial class AccountFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private const string SenhaValida = "uma-senha-bem-longa-2026";

    [GeneratedRegex(@"https://testes\.local/(?<caminho>[\w-]+)\?id=(?<id>[0-9a-f-]+)&token=(?<token>[^\s]+)")]
    private static partial Regex LinkDeConta { get; }

    [Fact]
    public async Task CadastroVerificacaoEEntradaFuncionamDePontaAPonta()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        using var client = await CriarClienteAsync(factory, cancellationToken);
        var email = EmailUnico();

        // 1. Cadastro responde 202 e dispara o e-mail de verificação.
        using var cadastro = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, displayName = "Pessoa de Teste", password = SenhaValida },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, cadastro.StatusCode);

        // 2. Entrar antes de confirmar é recusado, mesmo com a senha certa.
        using var antesDeConfirmar = await LoginAsync(client, email, SenhaValida, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, antesDeConfirmar.StatusCode);

        // 3. Confirmação a partir do link enviado.
        var (id, token) = ExtrairLink(emails.LastTo(email).TextBody, "verificar-email");
        using var confirmacao = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative),
            new { userId = id, token },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmacao.StatusCode);

        // 4. Agora a entrada funciona e /me devolve o perfil.
        using var entrada = await LoginAsync(client, email, SenhaValida, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, entrada.StatusCode);
        await AtualizarAntiforgeryAsync(client, cancellationToken);

        var perfil = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/v1/auth/me", UriKind.Relative),
            cancellationToken);
        Assert.Equal(email, perfil.GetProperty("email").GetString());
        Assert.True(perfil.GetProperty("emailConfirmed").GetBoolean());

        // 5. Depois do logout, /me volta a ser anônimo.
        using var saida = await client.PostAsync(
            new Uri("/api/v1/auth/logout", UriKind.Relative), null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, saida.StatusCode);

        using var depoisDoLogout = await client.GetAsync(
            new Uri("/api/v1/auth/me", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, depoisDoLogout.StatusCode);
    }

    [Fact]
    public async Task CadastroComEmailExistenteNaoSeDistingueDeCadastroNovo()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        using var client = await CriarClienteAsync(factory, cancellationToken);
        var email = EmailUnico();

        var corpo = new { email, displayName = "Primeira Pessoa", password = SenhaValida };
        using var primeiro = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative), corpo, cancellationToken);
        var textoPrimeiro = await primeiro.Content.ReadAsStringAsync(cancellationToken);

        using var segundo = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, displayName = "Outra Pessoa", password = SenhaValida },
            cancellationToken);
        var textoSegundo = await segundo.Content.ReadAsStringAsync(cancellationToken);

        // Status e corpo idênticos: a API não confirma que o e-mail já existe.
        Assert.Equal(primeiro.StatusCode, segundo.StatusCode);
        Assert.Equal(textoPrimeiro, textoSegundo);

        // O titular, porém, é avisado da tentativa.
        Assert.Equal("Já existe uma conta com este e-mail", emails.LastTo(email).Subject);
    }

    [Fact]
    public async Task SenhaErradaEEmailInexistenteRespondemIgual()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        using var client = await CriarClienteAsync(factory, cancellationToken);
        var email = EmailUnico();

        await CriarContaConfirmadaAsync(client, emails, email, cancellationToken);

        using var senhaErrada = await LoginAsync(client, email, "senha-errada-mas-longa", cancellationToken);
        var corpoSenhaErrada = await senhaErrada.Content.ReadAsStringAsync(cancellationToken);

        using var emailInexistente = await LoginAsync(
            client, EmailUnico(), "senha-errada-mas-longa", cancellationToken);
        var corpoInexistente = await emailInexistente.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal(senhaErrada.StatusCode, emailInexistente.StatusCode);
        Assert.Equal(Sem(corpoSenhaErrada, "traceId"), Sem(corpoInexistente, "traceId"));
    }

    [Fact]
    public async Task ContaBloqueadaNaoSeDistingueDeEmailInexistente()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        using var client = await CriarClienteAsync(factory, cancellationToken);
        var email = EmailUnico();

        await CriarContaConfirmadaAsync(client, emails, email, cancellationToken);

        // Cinco erros bloqueiam a conta (appsettings.json).
        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            using var erro = await LoginAsync(client, email, "senha-errada-mas-longa", cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, erro.StatusCode);
        }

        using var bloqueada = await LoginAsync(client, email, "senha-errada-mas-longa", cancellationToken);
        var corpoBloqueada = await bloqueada.Content.ReadAsStringAsync(cancellationToken);

        // Nem a senha certa muda a resposta: se mudasse, o bloqueio viraria um
        // jeito de confirmar o palpite em vez de interromper a tentativa.
        using var senhaCertaBloqueada = await LoginAsync(client, email, SenhaValida, cancellationToken);
        var corpoSenhaCerta = await senhaCertaBloqueada.Content.ReadAsStringAsync(cancellationToken);

        using var inexistente = await LoginAsync(
            client, EmailUnico(), "senha-errada-mas-longa", cancellationToken);
        var corpoInexistente = await inexistente.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, bloqueada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, senhaCertaBloqueada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inexistente.StatusCode);
        Assert.Equal(Sem(corpoInexistente, "traceId"), Sem(corpoBloqueada, "traceId"));
        Assert.Equal(Sem(corpoInexistente, "traceId"), Sem(corpoSenhaCerta, "traceId"));
    }

    [Fact]
    public async Task ContaNaoConfirmadaComSenhaErradaNaoSeDistingueDeEmailInexistente()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        using var client = await CriarClienteAsync(factory, cancellationToken);
        var email = EmailUnico();

        using var cadastro = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, displayName = "Ainda Sem Confirmar", password = SenhaValida },
            cancellationToken);
        cadastro.EnsureSuccessStatusCode();

        using var naoConfirmada = await LoginAsync(client, email, "senha-errada-mas-longa", cancellationToken);
        var corpoNaoConfirmada = await naoConfirmada.Content.ReadAsStringAsync(cancellationToken);

        using var inexistente = await LoginAsync(
            client, EmailUnico(), "senha-errada-mas-longa", cancellationToken);
        var corpoInexistente = await inexistente.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, naoConfirmada.StatusCode);
        Assert.Equal(Sem(corpoInexistente, "traceId"), Sem(corpoNaoConfirmada, "traceId"));
    }

    [Fact]
    public async Task EmailInexistenteCustaOMesmoHashDeUmaContaReal()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        var hasher = new CountingPasswordHasher();
        using var factory = sqlServer.CreateApi(emails, services =>
        {
            services.RemoveAll<IPasswordHasher<ApplicationUser>>();
            services.AddSingleton<IPasswordHasher<ApplicationUser>>(hasher);
        });
        using var client = await CriarClienteAsync(factory, cancellationToken);

        // Sem o hash, o e-mail inexistente responderia bem mais rápido, e o tempo
        // de resposta diria quais contas existem.
        using var resposta = await LoginAsync(
            client, EmailUnico(), "senha-errada-mas-longa", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal(1, hasher.Verifications);
    }

    [Fact]
    public async Task SessaoExpiraNoPrazoAbsolutoMesmoComUsoContinuo()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var factory = sqlServer.CreateApi(emails, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        using var client = await CriarClienteAsync(factory, cancellationToken);
        var email = EmailUnico();

        await CriarContaConfirmadaAsync(client, emails, email, cancellationToken);
        using var entrada = await LoginAsync(client, email, SenhaValida, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, entrada.StatusCode);

        // Uso a cada 9 dias renova a inatividade (14 dias) indefinidamente; só o
        // prazo absoluto (30 dias) encerra a sessão.
        foreach (var esperado in new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK })
        {
            clock.Advance(TimeSpan.FromDays(9));
            using var me = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), cancellationToken);
            Assert.Equal(esperado, me.StatusCode);
        }

        clock.Advance(TimeSpan.FromDays(4));
        using var expirada = await client.GetAsync(
            new Uri("/api/v1/auth/me", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, expirada.StatusCode);
    }

    [Fact]
    public async Task RecuperacaoDeSenhaInvalidaAsSessoesAbertas()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        using var client = await CriarClienteAsync(factory, cancellationToken);
        var email = EmailUnico();

        await CriarContaConfirmadaAsync(client, emails, email, cancellationToken);
        using var entrada = await LoginAsync(client, email, SenhaValida, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, entrada.StatusCode);
        await AtualizarAntiforgeryAsync(client, cancellationToken);

        using var pedido = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/forgot-password", UriKind.Relative),
            new { email },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, pedido.StatusCode);

        var (id, token) = ExtrairLink(emails.LastTo(email).TextBody, "recuperar-senha");
        using var redefinicao = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/reset-password", UriKind.Relative),
            new { userId = id, token, newPassword = "outra-senha-bem-longa-2026" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, redefinicao.StatusCode);

        // A sessão aberta antes da troca deixa de valer: o cookie carrega o
        // security stamp, e a redefinição o substitui.
        using var comCookieAntigo = await client.GetAsync(
            new Uri("/api/v1/auth/me", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, comCookieAntigo.StatusCode);

        // O mesmo link nao redefine a senha uma segunda vez, mesmo em outro
        // cliente e com um par de antiforgery novo.
        using var outroCliente = await CriarClienteAsync(factory, cancellationToken);
        using var repeticao = await outroCliente.PostAsJsonAsync(
            new Uri("/api/v1/auth/reset-password", UriKind.Relative),
            new { userId = id, token, newPassword = "terceira-senha-bem-longa-2026" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, repeticao.StatusCode);
    }

    [Fact]
    public async Task LinkDeConfirmacaoTemUsoUnico()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        using var client = await CriarClienteAsync(factory, cancellationToken);
        var email = EmailUnico();

        using var cadastro = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, displayName = "Uso Unico", password = SenhaValida },
            cancellationToken);
        cadastro.EnsureSuccessStatusCode();

        var (id, token) = ExtrairLink(emails.LastTo(email).TextBody, "verificar-email");
        var corpo = new { userId = id, token };

        using var primeira = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative), corpo, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);

        using var repeticao = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative), corpo, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, repeticao.StatusCode);
    }

    [Fact]
    public async Task LinkDeConfirmacaoExpira()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails, services =>
            services.Configure<EmailConfirmationTokenProviderOptions>(options =>
                options.TokenLifespan = TimeSpan.FromMilliseconds(50)));
        using var client = await CriarClienteAsync(factory, cancellationToken);
        var email = EmailUnico();

        using var cadastro = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, displayName = "Token Expirado", password = SenhaValida },
            cancellationToken);
        cadastro.EnsureSuccessStatusCode();

        var (id, token) = ExtrairLink(emails.LastTo(email).TextBody, "verificar-email");
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);

        using var confirmacao = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative),
            new { userId = id, token },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, confirmacao.StatusCode);
    }

    [Fact]
    public async Task LinkDeRecuperacaoExpira()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails, services =>
            services.Configure<DataProtectionTokenProviderOptions>(options =>
                options.TokenLifespan = TimeSpan.FromMilliseconds(50)));
        using var client = await CriarClienteAsync(factory, cancellationToken);
        var email = EmailUnico();

        await CriarContaConfirmadaAsync(client, emails, email, cancellationToken);
        using var pedido = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/forgot-password", UriKind.Relative),
            new { email },
            cancellationToken);
        pedido.EnsureSuccessStatusCode();

        var (id, token) = ExtrairLink(emails.LastTo(email).TextBody, "recuperar-senha");
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);

        using var redefinicao = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/reset-password", UriKind.Relative),
            new { userId = id, token, newPassword = "outra-senha-bem-longa-2026" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, redefinicao.StatusCode);
    }

    [Fact]
    public async Task RateLimitBloqueiaAbusoSemBloquearLeituraComum()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        using var client = await CriarClienteAsync(factory, cancellationToken);

        for (var tentativa = 0; tentativa < 10; tentativa++)
        {
            using var resposta = await LoginAsync(
                client, EmailUnico(), "senha-errada-mas-longa", cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        }

        using var limitada = await LoginAsync(
            client, EmailUnico(), "senha-errada-mas-longa", cancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitada.StatusCode);

        using var leitura = await client.GetAsync(
            new Uri("/api/v1/system/info", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
    }

    [Fact]
    public async Task RecuperacaoDeEmailInexistenteNaoRevelaNadaENaoEnviaMensagem()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        using var client = await CriarClienteAsync(factory, cancellationToken);

        using var resposta = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/forgot-password", UriKind.Relative),
            new { email = EmailUnico() },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        Assert.Empty(emails.Messages);
    }

    [Fact]
    public async Task MutacaoSemTokenAntiforgeryERecusada()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);

        // Cliente cru, sem passar pelo endpoint que emite o par de tokens.
        using var client = factory.CreateClient();

        using var resposta = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email = EmailUnico(), displayName = "Sem Token", password = SenhaValida },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Empty(emails.Messages);
    }

    [Fact]
    public async Task SenhaCurtaERecusadaComMotivo()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        using var factory = sqlServer.CreateApi(emails);
        using var client = await CriarClienteAsync(factory, cancellationToken);

        using var resposta = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email = EmailUnico(), displayName = "Senha Curta", password = "curta" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("12 caracteres", corpo, StringComparison.Ordinal);
    }

    private static async Task<HttpClient> CriarClienteAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken)
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

        await AtualizarAntiforgeryAsync(client, cancellationToken);
        return client;
    }

    /// <summary>
    /// Busca um par de tokens antiforgery novo, como o SPA faz ao carregar.
    ///
    /// Precisa ser refeito a cada mudanca de sessao: o token e vinculado a
    /// identidade autenticada, entao o que foi emitido para o anonimo deixa de
    /// valer depois da entrada, e vice-versa.
    /// </summary>
    private static async Task AtualizarAntiforgeryAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var resposta = await client.GetAsync(
            new Uri("/api/v1/auth/antiforgery", UriKind.Relative), cancellationToken);
        resposta.EnsureSuccessStatusCode();

        var requestToken = resposta.Headers
            .GetValues("Set-Cookie")
            .Select(cookie => Regex.Match(cookie, @"XSRF-TOKEN=(?<valor>[^;]+)"))
            .First(match => match.Success)
            .Groups["valor"].Value;

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", HttpUtility.UrlDecode(requestToken));
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string senha,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = senha },
            cancellationToken);

    private static async Task CriarContaConfirmadaAsync(
        HttpClient client,
        CapturingEmailSender emails,
        string email,
        CancellationToken cancellationToken)
    {
        using var cadastro = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, displayName = "Conta de Teste", password = SenhaValida },
            cancellationToken);
        cadastro.EnsureSuccessStatusCode();

        var (id, token) = ExtrairLink(emails.LastTo(email).TextBody, "verificar-email");
        using var confirmacao = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative),
            new { userId = id, token },
            cancellationToken);
        confirmacao.EnsureSuccessStatusCode();
    }

    private static (Guid Id, string Token) ExtrairLink(string corpo, string caminhoEsperado)
    {
        var match = LinkDeConta.Match(corpo);
        Assert.True(match.Success, $"Nenhum link encontrado no corpo: {corpo}");
        Assert.Equal(caminhoEsperado, match.Groups["caminho"].Value);

        return (Guid.Parse(match.Groups["id"].Value), HttpUtility.UrlDecode(match.Groups["token"].Value));
    }

    /// <summary>Remove a propriedade indicada para comparar dois corpos que só diferem nela.</summary>
    private static string Sem(string json, string propriedade)
    {
        using var documento = JsonDocument.Parse(json);
        var restante = documento.RootElement.EnumerateObject()
            .Where(item => !string.Equals(item.Name, propriedade, StringComparison.Ordinal))
            .ToDictionary(item => item.Name, item => item.Value.ToString(), StringComparer.Ordinal);

        return JsonSerializer.Serialize(restante);
    }

    private static string EmailUnico() => $"teste-{Guid.CreateVersion7():n}@exemplo.local";

    /// <summary>Hasher real que conta quantas senhas foram conferidas.</summary>
    private sealed class CountingPasswordHasher : IPasswordHasher<ApplicationUser>
    {
        private readonly PasswordHasher<ApplicationUser> _inner = new();
        private int _verifications;

        public int Verifications => Volatile.Read(ref _verifications);

        public string HashPassword(ApplicationUser user, string password) =>
            _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(
            ApplicationUser user,
            string hashedPassword,
            string providedPassword)
        {
            Interlocked.Increment(ref _verifications);
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
