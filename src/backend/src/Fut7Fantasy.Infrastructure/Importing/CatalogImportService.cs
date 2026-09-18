using System.Data;
using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Importing;
using Fut7Fantasy.Domain.Importing;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Importing;

public sealed partial class CatalogImportService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : ICatalogImportService
{
    public Task<ImportResult> PreviewAsync(
        Guid competitionId,
        ImportKind kind,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        RunAsync(competitionId, kind, content, apply: false, cancellationToken);

    public Task<ImportResult> CommitAsync(
        Guid competitionId,
        ImportKind kind,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        RunAsync(competitionId, kind, content, apply: true, cancellationToken);

    /// <summary>
    /// Pré-visualizar e confirmar percorrem exatamente o mesmo caminho; só o último passo
    /// difere. Dois caminhos separados divergiriam com o tempo, e a pré-visualização
    /// deixaria de valer como promessa do que a confirmação faz.
    /// </summary>
    private async Task<ImportResult> RunAsync(
        Guid competitionId,
        ImportKind kind,
        ReadOnlyMemory<byte> content,
        bool apply,
        CancellationToken cancellationToken)
    {
        var read = CsvReader.Read(content.Span);
        if (read.Document is null)
        {
            return ImportResult.Rejected(Explain(read.Failure!.Value, read.Line));
        }

        if (!await dbContext.Competitions
            .AnyAsync(competition => competition.Id == competitionId, cancellationToken)
            .ConfigureAwait(false))
        {
            return ImportResult.Of(ImportOutcome.NotFound);
        }

        // A trava na linha do campeonato serializa importações do mesmo campeonato: sem
        // ela, dois arquivos simultâneos criariam o mesmo time duas vezes.
        return await InCompetitionLockAsync(competitionId, async () => kind switch
        {
            ImportKind.Teams => await TeamsAsync(competitionId, read.Document, apply, cancellationToken)
                .ConfigureAwait(false),
            ImportKind.Athletes => await AthletesAsync(competitionId, read.Document, apply, cancellationToken)
                .ConfigureAwait(false),
            ImportKind.Coaches => await CoachesAsync(competitionId, read.Document, apply, cancellationToken)
                .ConfigureAwait(false),
            ImportKind.Matches => await MatchesAsync(competitionId, read.Document, apply, cancellationToken)
                .ConfigureAwait(false),
            _ => ImportResult.Of(ImportOutcome.NotFound),
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImportResult> TeamsAsync(
        Guid competitionId,
        CsvDocument document,
        bool apply,
        CancellationToken cancellationToken)
    {
        var parsed = ImportParser.Teams(document);
        if (!parsed.IsValid)
        {
            return ImportResult.Invalid(parsed.Issues);
        }

        var existing = await dbContext.RealTeams
            .Where(team => team.CompetitionId == competitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byName = existing.ToDictionary(team => team.Name, StringComparer.OrdinalIgnoreCase);

        List<ImportIssue> issues = [];
        List<TeamImportRow> created = [];
        var unchanged = 0;
        foreach (var row in parsed.Rows)
        {
            if (!byName.TryGetValue(row.Name, out var team))
            {
                created.Add(row);
                continue;
            }

            if (team.IsArchived)
            {
                issues.Add(new(
                    row.Line,
                    "nome",
                    $"`{row.Name}` está arquivado neste campeonato. Use outro nome ou "
                    + "reative o time antes de importar."));
                continue;
            }

            // Nome é a única coisa que o template traz, então o time existente já está igual.
            unchanged++;
        }

        if (issues.Count > 0)
        {
            return ImportResult.Invalid(issues);
        }

        var summary = new ImportSummary(parsed.Rows.Count, created.Count, 0, unchanged);
        if (!apply)
        {
            return Completed(summary);
        }

        var now = clock.GetUtcNow();
        foreach (var row in created)
        {
            var team = RealTeam.Create(
                Guid.CreateVersion7(), competitionId, new RealTeamDefinition(row.Name), now);

            // O técnico nasce com o time, como no cadastro manual: um time sem técnico
            // seria um elenco impossível de escalar.
            var coach = Coach.Create(
                Guid.CreateVersion7(),
                competitionId,
                team.Id,
                new CoachDefinition(null, PriceTier.Regular, null),
                now);
            dbContext.RealTeams.Add(team);
            dbContext.Coaches.Add(coach);
        }

        return await SaveAsync(competitionId, ImportKind.Teams, summary, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ImportResult> AthletesAsync(
        Guid competitionId,
        CsvDocument document,
        bool apply,
        CancellationToken cancellationToken)
    {
        var parsed = ImportParser.Athletes(document);
        if (!parsed.IsValid)
        {
            return ImportResult.Invalid(parsed.Issues);
        }

        var teams = await dbContext.RealTeams
            .Where(team => team.CompetitionId == competitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var teamsByName = teams.ToDictionary(team => team.Name, StringComparer.OrdinalIgnoreCase);
        var current = await (
            from athlete in dbContext.Athletes
            join registration in dbContext.RosterRegistrations
                on athlete.Id equals registration.AthleteId
            where athlete.CompetitionId == competitionId
            select new { Athlete = athlete, Registration = registration })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byName = current.ToDictionary(row => row.Athlete.SportingName, StringComparer.OrdinalIgnoreCase);

        List<ImportIssue> issues = [];
        List<(AthleteImportRow Row, Guid TeamId)> created = [];
        List<AthleteImportRow> updated = [];
        var unchanged = 0;

        foreach (var row in parsed.Rows)
        {
            if (!teamsByName.TryGetValue(row.TeamName, out var team))
            {
                issues.Add(new(
                    row.Line,
                    "time",
                    $"`{row.TeamName}` não é um time deste campeonato. Importe os times antes dos atletas."));
                continue;
            }

            if (!byName.TryGetValue(row.SportingName, out var existing))
            {
                if (team.IsArchived)
                {
                    issues.Add(new(row.Line, "time", $"`{row.TeamName}` está arquivado e não recebe atletas."));
                    continue;
                }

                created.Add((row, team.Id));
                continue;
            }

            if (existing.Registration.RealTeamId != team.Id)
            {
                issues.Add(new(
                    row.Line,
                    "time",
                    $"`{row.SportingName}` já está inscrito em outro time. "
                    + "Atleta não é transferido entre times no mesmo campeonato."));
                continue;
            }

            if (!existing.Registration.IsActive)
            {
                issues.Add(new(
                    row.Line,
                    "nome_esportivo",
                    $"`{row.SportingName}` foi desligado. O histórico é preservado, "
                    + "então a importação não o reativa."));
                continue;
            }

            if (existing.Athlete.FirstMarketAvailableAt is not null
                && existing.Athlete.Position != row.Position)
            {
                issues.Add(new(
                    row.Line,
                    "posicao",
                    $"A posição de `{row.SportingName}` não muda depois que ele entrou no mercado."));
                continue;
            }

            if (Matches(existing.Athlete, existing.Registration, row))
            {
                unchanged++;
                continue;
            }

            updated.Add(row);
        }

        if (issues.Count > 0)
        {
            return ImportResult.Invalid(issues);
        }

        var summary = new ImportSummary(parsed.Rows.Count, created.Count, updated.Count, unchanged);
        if (!apply)
        {
            return Completed(summary);
        }

        var now = clock.GetUtcNow();
        foreach (var (row, teamId) in created)
        {
            var definition = Definition(row);
            var athlete = Athlete.Create(Guid.CreateVersion7(), competitionId, definition, now);
            dbContext.Athletes.Add(athlete);
            dbContext.RosterRegistrations.Add(RosterRegistration.Create(
                Guid.CreateVersion7(), competitionId, athlete.Id, teamId, definition, now));
        }

        foreach (var row in updated)
        {
            var existing = byName[row.SportingName];
            var definition = Definition(row);
            existing.Athlete.Update(definition, now);
            existing.Registration.UpdatePricing(definition);
        }

        return await SaveAsync(competitionId, ImportKind.Athletes, summary, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ImportResult> CoachesAsync(
        Guid competitionId,
        CsvDocument document,
        bool apply,
        CancellationToken cancellationToken)
    {
        var parsed = ImportParser.Coaches(document);
        if (!parsed.IsValid)
        {
            return ImportResult.Invalid(parsed.Issues);
        }

        var rows = await (
            from coach in dbContext.Coaches
            join team in dbContext.RealTeams on coach.RealTeamId equals team.Id
            where coach.CompetitionId == competitionId
            select new { Coach = coach, Team = team })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byTeam = rows.ToDictionary(row => row.Team.Name, StringComparer.OrdinalIgnoreCase);

        List<ImportIssue> issues = [];
        List<CoachImportRow> updated = [];
        var unchanged = 0;
        foreach (var row in parsed.Rows)
        {
            if (!byTeam.TryGetValue(row.TeamName, out var existing))
            {
                issues.Add(new(
                    row.Line,
                    "time",
                    $"`{row.TeamName}` não é um time deste campeonato. Importe os times antes dos técnicos."));
                continue;
            }

            if (existing.Team.IsArchived)
            {
                issues.Add(new(row.Line, "time", $"`{row.TeamName}` está arquivado e não recebe alteração."));
                continue;
            }

            if (Matches(existing.Coach, row))
            {
                unchanged++;
                continue;
            }

            updated.Add(row);
        }

        if (issues.Count > 0)
        {
            return ImportResult.Invalid(issues);
        }

        // O técnico já existe desde a criação do time, então nunca há criação aqui.
        var summary = new ImportSummary(parsed.Rows.Count, 0, updated.Count, unchanged);
        if (!apply)
        {
            return Completed(summary);
        }

        var now = clock.GetUtcNow();
        foreach (var row in updated)
        {
            byTeam[row.TeamName].Coach.Update(
                new CoachDefinition(row.DisplayName, row.PriceTier, row.ExactPrice), now);
        }

        return await SaveAsync(competitionId, ImportKind.Coaches, summary, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private static AthleteDefinition Definition(AthleteImportRow row) =>
        new(row.SportingName, row.Position, row.PriceTier, row.ExactPrice);

    private static bool Matches(Athlete athlete, RosterRegistration registration, AthleteImportRow row) =>
        athlete.Position == row.Position
        && registration.PriceTier == row.PriceTier
        && registration.InitialPriceOverride == row.ExactPrice
        && string.Equals(athlete.SportingName, row.SportingName, StringComparison.Ordinal);

    private static bool Matches(Coach coach, CoachImportRow row) =>
        coach.DisplayName == row.DisplayName
        && coach.PriceTier == row.PriceTier
        && coach.InitialPriceOverride == row.ExactPrice;

    private static ImportResult Completed(ImportSummary summary) =>
        new(ImportOutcome.Completed, summary, [], null);

    private async Task<ImportResult> SaveAsync(
        Guid competitionId,
        ImportKind kind,
        ImportSummary summary,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? notes = null)
    {
        // A auditoria guarda o resumo, nunca o conteúdo do arquivo (04 §9).
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            currentUser.Id ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada."),
            $"CatalogImported{kind}",
            competitionId,
            $"Importação de {ImportTemplates.For(kind).Label}: {summary.Created} "
            + $"criados, {summary.Updated} alterados, {summary.Unchanged} sem mudança.",
            now));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Completed(summary) with { Notes = notes ?? [] };
    }

    private static string Explain(CsvFailure failure, int line) => failure switch
    {
        CsvFailure.Empty => "O arquivo está vazio.",
        CsvFailure.TooLarge =>
            $"O arquivo passa de {CsvLimits.MaxBytes / 1024} KB. Divida a planilha em partes.",
        CsvFailure.TooManyRows =>
            $"O arquivo passa de {CsvLimits.MaxRows} linhas. Divida a planilha em partes.",
        CsvFailure.TooManyColumns =>
            $"A linha {line} tem colunas demais. Baixe o modelo e refaça o arquivo.",
        CsvFailure.CellTooLong =>
            $"A linha {line} tem uma célula com mais de {CsvLimits.MaxCellLength} caracteres.",
        CsvFailure.InvalidEncoding =>
            "O arquivo não está em UTF-8. Na planilha, salve como CSV UTF-8.",
        CsvFailure.UnterminatedQuote =>
            $"As aspas abertas na linha {line} não foram fechadas.",
        _ => "Não foi possível ler o arquivo.",
    };

    /// <summary>
    /// Serializa as importações de um mesmo campeonato. A trava fica na linha do
    /// campeonato, como no cadastro manual: read committed com UPDLOCK, sem range locks.
    /// </summary>
    private Task<T> InCompetitionLockAsync<T>(
        Guid competitionId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

            await dbContext.Competitions
                .FromSql($"""
                    SELECT * FROM [competitions].[Competitions] WITH (UPDLOCK, ROWLOCK)
                    WHERE [Id] = {competitionId}
                    """)
                .AsNoTracking()
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = await operation().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        });
    }
}
