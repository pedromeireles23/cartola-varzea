namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>
/// Sessão de entrada guardada no servidor (04-seguranca §5). O cookie leva só a chave
/// desta linha; apagá-la encerra a sessão, mesmo que o navegador ainda tenha o cookie.
/// </summary>
public sealed class UserSession
{
    /// <summary>Chave aleatória que viaja, protegida, dentro do cookie.</summary>
    public required Guid Id { get; init; }

    /// <summary>Dono da sessão; permite encerrar todas as sessões de uma conta.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Ticket de autenticação serializado, com claims e propriedades.</summary>
    public required byte[] Ticket { get; set; }

    /// <summary>Fim da validade, renovado junto com o cookie deslizante.</summary>
    public required DateTimeOffset ExpiresAt { get; set; }
}
