namespace Fut7Fantasy.Domain.Leagues;

/// <summary>Um problema de validação da liga, ligado ao campo que o causou.</summary>
public sealed record LeagueError(string Field, string Message);

/// <summary>O que quem cria a liga escolhe.</summary>
public sealed record LeagueDefinition(string Name)
{
    public const int NameMinLength = 3;
    public const int NameMaxLength = 60;

    public LeagueDefinition Normalized() => this with { Name = Name?.Trim() ?? string.Empty };

    public IReadOnlyList<LeagueError> Validate() =>
        Normalized().Name.Length is < NameMinLength or > NameMaxLength
            ? [new(nameof(Name), $"Use de {NameMinLength} a {NameMaxLength} caracteres no nome da liga.")]
            : [];
}

/// <summary>
/// Uma liga privada dentro de um campeonato (01 §9): um recorte do ranking geral entre
/// quem se conhece. A pontuação é a mesma — a liga não muda regra nenhuma, só o conjunto
/// de participações que entram na conta.
///
/// A liga pertence ao campeonato, e não à organização: a equipe de alguém vale num
/// campeonato só, então uma liga nunca pode misturar campeonatos (04 §6).
/// </summary>
public sealed class PrivateLeague
{
    /// <summary>
    /// Acima disso a liga deixa de ser "entre quem se conhece" e o ranking geral já
    /// resolve. O limite também segura o custo de um código vazado.
    /// </summary>
    public const int MaxMembers = 200;

    private PrivateLeague()
    {
    }

    private PrivateLeague(
        Guid id,
        Guid competitionId,
        Guid ownerUserId,
        LeagueDefinition definition,
        string inviteCode,
        DateTimeOffset createdAt)
    {
        Id = id;
        CompetitionId = competitionId;
        OwnerUserId = ownerUserId;
        InviteCode = inviteCode;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Apply(definition);
    }

    public Guid Id { get; private set; }

    public Guid CompetitionId { get; private set; }

    /// <summary>Quem criou. Não sai da liga sem apagá-la: liga sem dono não existe.</summary>
    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// O código que entra na liga, legível de propósito (ver <see cref="LeagueInviteCode"/>).
    /// Nulo quando o dono fechou a liga: sem código, ninguém novo entra.
    /// </summary>
    public string? InviteCode { get; private set; }

    /// <summary>Fim do prazo do código atual; nulo quando ele não expira.</summary>
    public DateTimeOffset? InviteExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static PrivateLeague Create(
        Guid id,
        Guid competitionId,
        Guid ownerUserId,
        LeagueDefinition definition,
        string inviteCode,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteCode);
        if (id == Guid.Empty || competitionId == Guid.Empty || ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Liga, campeonato e dono precisam ser identificados.");
        }

        return new(id, competitionId, ownerUserId, definition, inviteCode, createdAt);
    }

    public void Rename(LeagueDefinition definition, DateTimeOffset now)
    {
        Apply(definition);
        UpdatedAt = now;
    }

    /// <summary>
    /// Troca o código por outro. É o que se faz quando o antigo vazou ou quando o dono
    /// só quer parar de receber gente: quem já entrou continua na liga.
    /// </summary>
    public void RotateInvite(string inviteCode, DateTimeOffset now, DateTimeOffset? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteCode);
        if (expiresAt is { } instant && instant <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "O prazo do convite precisa ser futuro.");
        }

        InviteCode = inviteCode;
        InviteExpiresAt = expiresAt;
        UpdatedAt = now;
    }

    /// <summary>Fecha a liga para novas entradas sem mexer em quem já está dentro.</summary>
    public void CloseInvite(DateTimeOffset now)
    {
        InviteCode = null;
        InviteExpiresAt = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Verdadeiro quando o código vale agora. Um código fechado ou vencido recusa a
    /// entrada com a mesma resposta de um código errado, para não confirmar que a liga
    /// existe (04 §6).
    /// </summary>
    public bool AcceptsJoin(DateTimeOffset now) =>
        InviteCode is not null && (InviteExpiresAt is not { } expiresAt || now < expiresAt);

    private void Apply(LeagueDefinition definition)
    {
        if (definition.Validate() is [var first, ..])
        {
            throw new ArgumentException(first.Message, nameof(definition));
        }

        Name = definition.Normalized().Name;
    }
}

/// <summary>
/// A participação de uma conta numa liga. Ela aponta para a `FantasyEntry`, não para a
/// conta: é a equipe daquele campeonato que disputa, e por isso uma liga nunca alcança
/// a equipe de outro campeonato.
/// </summary>
public sealed class LeagueMembership
{
    private LeagueMembership()
    {
    }

    private LeagueMembership(Guid id, Guid leagueId, Guid entryId, Guid userId, DateTimeOffset joinedAt)
    {
        Id = id;
        LeagueId = leagueId;
        EntryId = entryId;
        UserId = userId;
        JoinedAt = joinedAt;
    }

    public Guid Id { get; private set; }

    public Guid LeagueId { get; private set; }

    public Guid EntryId { get; private set; }

    /// <summary>Repetido da participação para a liga ser lida sem uma junção a mais.</summary>
    public Guid UserId { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public static LeagueMembership Create(
        Guid id,
        Guid leagueId,
        Guid entryId,
        Guid userId,
        DateTimeOffset joinedAt)
    {
        if (id == Guid.Empty || leagueId == Guid.Empty || entryId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Associação, liga, participação e conta precisam ser identificadas.");
        }

        return new(id, leagueId, entryId, userId, joinedAt);
    }
}
