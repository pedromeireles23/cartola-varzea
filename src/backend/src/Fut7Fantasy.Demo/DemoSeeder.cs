using System.Globalization;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Application.Fantasy;
using Fut7Fantasy.Application.Importing;
using Fut7Fantasy.Application.Leagues;
using Fut7Fantasy.Application.PlatformAdministration;
using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Application.SportsCatalog;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.Importing;
using Fut7Fantasy.Domain.Leagues;
using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Infrastructure.Identity;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fut7Fantasy.Demo;

/// <summary>O que o seed deixou pronto, para o terminal e para os testes.</summary>
internal sealed record DemoSummary(string Slug, Guid CompetitionId, Guid OrganizationId, int Entries);

/// <summary>
/// Conta a história da Copa da Vila pelos serviços da aplicação, do cadastro da conta à
/// correção de uma rodada (Fase 12). Cada passo roda num escopo novo, como uma requisição
/// da API, no nome de quem agiria — organização, administração ou quem joga — e na hora
/// em que agiria: o relógio só anda para frente.
///
/// A linha do tempo é relativa a <c>agora</c>: duas rodadas publicadas e consolidadas (a
/// segunda corrigida), uma publicada ainda provisória, uma em conferência, uma com o
/// mercado aberto e o returno em rascunho.
/// </summary>
internal sealed class DemoSeeder(
    IServiceProvider services,
    DemoClock clock,
    DemoActor actor,
    IOptions<DemoOptions> options)
{
    private static readonly string[] StarterOrder =
        ["Goalkeeper", "Defender", "Defender", "Midfielder", "Midfielder", "Forward", "Forward"];
    private static readonly string[] BenchOrder = ["Goalkeeper", "Defender", "Midfielder", "Forward"];

    private readonly Random _random = new(2026);
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(DemoUniverse.TimeZoneId);
    private readonly Dictionary<string, Guid> _accounts = new(StringComparer.Ordinal);

    private DateOnly _today;
    private Guid _organizerId;
    private Guid _adminId;
    private Guid _viewerId;
    private Guid _competitionId;
    private Guid _stageId;
    private string _slug = string.Empty;
    private List<Guid> _teams = [];
    private readonly List<Guid> _rounds = [];

    public async Task<DemoSummary> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        _today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _zone).DateTime);

        At(-23, 9, 0);
        await CreateAccountsAsync(cancellationToken);
        var organizationId = await CreateOrganizationAsync(cancellationToken);

        At(-23, 9, 30);
        await CreateCompetitionAsync(organizationId, cancellationToken);
        await AddViewerAsAssistantAsync(organizationId, cancellationToken);

        At(-22, 20, 0);
        await CreateRoundsAsync(cancellationToken);

        // Rodada 1: mercado aberto, todo mundo monta o time, as ligas nascem.
        At(-21, 12, 0);
        await ChangeStatusAsync(0, RoundTransition.OpenMarket, cancellationToken);
        var players = DemoUniverse.Players.Take(DemoUniverse.Players.Count - DemoUniverse.LateJoiners).ToList();
        foreach (var (player, index) in players.Select((player, index) => (player, index)))
        {
            At(-21, 13, index * 3);
            await JoinWithSquadAsync(_accounts[player.Email], cancellationToken);
        }

        At(-20, 19, 0);
        await JoinWithSquadAsync(_viewerId, cancellationToken);
        await CreateLeaguesAsync(cancellationToken);

        await PlayAndPublishAsync(0, matchDay: -16, cancellationToken);

        // Rodada 2: alguns ajustam o time; depois de publicada, a liga corrige uma súmula.
        At(-15, 11, 0);
        await ChangeStatusAsync(1, RoundTransition.OpenMarket, cancellationToken);
        At(-13, 19, 0);
        await TransfersAsync(players.Take(4), cancellationToken);
        await PlayAndPublishAsync(1, matchDay: -9, cancellationToken);

        // Rodada 3: quem chegou atrasado entra; a correção da Rodada 2 sai enquanto isso.
        At(-8, 11, 0);
        await ChangeStatusAsync(2, RoundTransition.OpenMarket, cancellationToken);
        At(-7, 10, 0);
        await CorrectRoundAsync(1, cancellationToken);
        var late = DemoUniverse.Players.Skip(players.Count).ToList();
        for (var index = 0; index < late.Count; index++)
        {
            var player = late[index];
            At(-6, 20, index * 5);
            await JoinWithSquadAsync(_accounts[player.Email], cancellationToken);
        }

        At(-5, 19, 0);
        await TransfersAsync(players.Skip(4).Take(4), cancellationToken);
        await PlayAndPublishAsync(2, matchDay: -2, cancellationToken, publishHour: 18);

        // Rodada 4 jogada ontem, com a súmula em conferência; Rodada 5 com o mercado aberto.
        At(-2, 18, 30);
        await ChangeStatusAsync(3, RoundTransition.OpenMarket, cancellationToken);
        At(-1, 14, 0);
        await FillSheetsAsync(3, cancellationToken);
        await ChangeStatusAsync(3, RoundTransition.SendToReview, cancellationToken);
        At(-1, 16, 0);
        await ChangeStatusAsync(4, RoundTransition.OpenMarket, cancellationToken);

        clock.AdvanceTo(now);
        return new DemoSummary(_slug, _competitionId, organizationId, DemoUniverse.Players.Count + 1);
    }

    private async Task CreateAccountsAsync(CancellationToken cancellationToken)
    {
        var passwords = options.Value;
        _adminId = await CreateUserAsync(DemoUniverse.Admin, passwords.AdminPassword, ApplicationRole.PlatformAdmin);
        _organizerId = await CreateUserAsync(DemoUniverse.Organizer, passwords.OrganizerPassword, role: null);
        _viewerId = await CreateUserAsync(DemoUniverse.Viewer, passwords.ViewerPassword, ApplicationRole.DemoViewer);

        foreach (var player in DemoUniverse.Players)
        {
            _accounts[player.Email] = await CreateUserAsync(player, password: null, role: null);
        }

        foreach (var (person, organization) in DemoUniverse.PendingApplications)
        {
            var applicant = await CreateUserAsync(person, password: null, role: null);
            await AsAsync(applicant, [], async provider =>
            {
                var submitted = await provider.GetRequiredService<IOrganizerApplicationService>()
                    .SubmitAsync(organization, cancellationToken);
                Ensure(submitted.Created, $"pedido de acesso de {person.DisplayName}");
            });
        }
    }

    private async Task<Guid> CreateUserAsync(DemoPerson person, string? password, string? role)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(clock.GetUtcNow()),
            UserName = person.Email,
            Email = person.Email,
            EmailConfirmed = true,
            DisplayName = person.DisplayName,
            CreatedAt = clock.GetUtcNow(),
        };

        // Quem joga pela demo não tem senha: ninguém entra por essas contas.
        var created = password is null
            ? await users.CreateAsync(user).ConfigureAwait(false)
            : await users.CreateAsync(user, password).ConfigureAwait(false);
        Ensure(created.Succeeded, $"conta {person.Email}: {Describe(created)}");

        if (role is not null)
        {
            if (!await roles.RoleExistsAsync(role).ConfigureAwait(false))
            {
                var roleCreated = await roles.CreateAsync(new ApplicationRole
                {
                    Id = Guid.CreateVersion7(clock.GetUtcNow()),
                    Name = role,
                }).ConfigureAwait(false);
                Ensure(roleCreated.Succeeded, $"papel {role}");
            }

            var added = await users.AddToRoleAsync(user, role).ConfigureAwait(false);
            Ensure(added.Succeeded, $"papel {role} de {person.Email}");
        }

        return user.Id;
    }

    /// <summary>A organização nasce pelo caminho real: pedido de acesso e aprovação.</summary>
    private async Task<Guid> CreateOrganizationAsync(CancellationToken cancellationToken)
    {
        Guid applicationId = Guid.Empty;
        await AsAsync(_organizerId, [], async provider =>
        {
            var submitted = await provider.GetRequiredService<IOrganizerApplicationService>()
                .SubmitAsync(DemoUniverse.OrganizationName, cancellationToken);
            Ensure(submitted.Created, "pedido de acesso da organização");
            applicationId = submitted.Application.Id;
        });

        At(-23, 9, 15);
        Guid? organizationId = null;
        await AsAsync(_adminId, [ApplicationRole.PlatformAdmin], async provider =>
        {
            var approved = await provider.GetRequiredService<IOrganizerApplicationService>()
                .ApproveAsync(applicationId, "Liga conhecida, aprovada para a temporada 2026.", cancellationToken);
            Ensure(approved.Outcome == ReviewOutcome.Completed, "aprovação da organização");
            organizationId = approved.Application?.OrganizationId;
        });

        return organizationId ?? throw new InvalidOperationException("A aprovação não devolveu a organização.");
    }

    private async Task CreateCompetitionAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await AsOrganizerAsync(async provider =>
        {
            var created = await provider.GetRequiredService<ICompetitionManagementService>().CreateDraftAsync(
                organizationId,
                new CompetitionSettings(
                    DemoUniverse.CompetitionName,
                    DemoUniverse.Season,
                    Modality.Fut7,
                    DemoUniverse.TimeZoneId,
                    MarketCloseLeadTime: TimeSpan.FromHours(1),
                    ResultsSlaBusinessDays: 2,
                    CorrectionWindowBusinessDays: 3),
                cancellationToken);
            Ensure(
                created.Outcome == CompetitionCommandOutcome.Completed,
                "campeonato",
                created.Errors.Select(e => e.Message));
            _competitionId = created.Competition!.Id;
        });

        await AsOrganizerAsync(async provider =>
        {
            var imports = provider.GetRequiredService<ICatalogImportService>();
            foreach (var (kind, file) in new[]
            {
                (ImportKind.Teams, "times-v1.csv"),
                (ImportKind.Athletes, "atletas-v1.csv"),
                (ImportKind.Coaches, "tecnicos-v1.csv"),
            })
            {
                var result = await imports.CommitAsync(_competitionId, kind, ReadDemoFile(file), cancellationToken);
                Ensure(
                    result.Outcome == ImportOutcome.Completed,
                    $"importação de {file}",
                    result.Issues.Select(issue => $"linha {issue.Line}: {issue.Message}"));
            }
        });

        // A ordem do arquivo de times é a ordem do turno em DemoUniverse.Fixtures.
        var names = DemoFileLines("times-v1.csv")
            .Skip(1)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        await AsOrganizerAsync(async provider =>
        {
            var teams = await provider.GetRequiredService<IRealTeamService>()
                .ListAsync(_competitionId, cancellationToken);
            _teams = [.. names.Select(name => teams.Single(team => team.Name == name).Id)];

            var stages = provider.GetRequiredService<ICompetitionStageService>();
            var stage = await stages.CreateAsync(
                _competitionId,
                new StageDefinition(
                    DemoUniverse.StageName,
                    StageFormat.Groups,
                    [new GroupDefinition(null, DemoUniverse.GroupName)],
                    StageDefinition.DefaultTiebreakers),
                cancellationToken);
            Ensure(stage.Outcome == StageCommandOutcome.Completed, "fase", stage.Errors.Select(e => e.Message));
            _stageId = stage.Stage!.Id;

            var groupId = stage.Stage.Groups[0].Id;
            var participants = await stages.SetParticipantsAsync(
                _competitionId,
                _stageId,
                [.. _teams.Select(team => new StageParticipantAssignment(team, groupId))],
                stage.Stage.Version,
                cancellationToken);
            Ensure(
                participants.Outcome == StageCommandOutcome.Completed,
                "times da fase",
                participants.Errors.Select(e => e.Message));
        });

        await AsOrganizerAsync(async provider =>
        {
            var publication = provider.GetRequiredService<ICompetitionPublicationService>();
            var readiness = await publication.GetReadinessAsync(_competitionId, cancellationToken)
                ?? throw new InvalidOperationException("Campeonato sumiu antes da publicação.");
            var published = await publication.SetPublishedAsync(
                _competitionId,
                published: true,
                readiness.Version,
                cancellationToken);
            Ensure(
                published.Outcome == CompetitionPublicationOutcome.Completed,
                "publicação do campeonato",
                published.Readiness?.Items.Select(item => item.Message) ?? []);
            _slug = published.Readiness!.Slug!;
        });
    }

    /// <summary>
    /// O visitante auxilia a organização para enxergar a área de quem organiza. Sem convite
    /// por e-mail — o seed não manda mensagem —, a associação é gravada direto.
    /// </summary>
    private async Task AddViewerAsAssistantAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        dbContext.OrganizationMembers.Add(
            OrganizationMember.CreateAssistant(organizationId, _viewerId, clock.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>O turno inteiro em cinco rodadas, mais a primeira do returno em rascunho.</summary>
    private async Task CreateRoundsAsync(CancellationToken cancellationToken)
    {
        // Dia de cada rodada, relativo a hoje; a sexta é a primeira do returno, com o mando invertido.
        int[] matchDays = [-16, -9, -2, -1, 4, 11];
        var fixtures = DemoUniverse.Fixtures
            .Append([.. DemoUniverse.Fixtures[0].Select(game => (Home: game.Away, Away: game.Home))])
            .ToList();

        await AsOrganizerAsync(async provider =>
        {
            var rounds = provider.GetRequiredService<ICompetitionRoundService>();
            for (var index = 0; index < fixtures.Count; index++)
            {
                var name = $"Rodada {index + 1}";
                var created = await rounds.CreateAsync(_competitionId, new RoundDefinition(name), cancellationToken);
                Ensure(created.Outcome == RoundCommandOutcome.Completed, name, created.Errors.Select(e => e.Message));
                var round = created.Round!;

                for (var slot = 0; slot < fixtures[index].Count; slot++)
                {
                    var (home, away) = fixtures[index][slot];
                    var kickoff = _today.AddDays(matchDays[index]).ToDateTime(DemoUniverse.KickoffTimes[slot]);
                    var added = await rounds.AddMatchAsync(
                        _competitionId,
                        round.Id,
                        new MatchInput(
                            _stageId,
                            _teams[home],
                            _teams[away],
                            kickoff.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
                            Status: null),
                        round.Version,
                        cancellationToken);
                    Ensure(
                        added.Outcome == RoundCommandOutcome.Completed,
                        $"partida da {name}",
                        added.Errors.Select(e => e.Message));
                    round = added.Round!;
                }

                _rounds.Add(round.Id);
            }
        });
    }

    private async Task ChangeStatusAsync(int round, RoundTransition transition, CancellationToken cancellationToken)
    {
        await AsOrganizerAsync(async provider =>
        {
            var rounds = provider.GetRequiredService<ICompetitionRoundService>();
            var current = await CurrentRoundAsync(rounds, round, cancellationToken);
            var changed = await rounds.ChangeStatusAsync(
                _competitionId,
                current.Id,
                transition,
                current.Version,
                cancellationToken);
            Ensure(
                changed.Outcome == RoundCommandOutcome.Completed,
                $"{transition} da Rodada {round + 1}",
                changed.Errors.Select(e => e.Message));
        });
    }

    /// <summary>Joga a rodada no dia marcado, lança as súmulas, confere e publica no dia seguinte.</summary>
    private async Task PlayAndPublishAsync(
        int round,
        int matchDay,
        CancellationToken cancellationToken,
        int publishHour = 10)
    {
        At(matchDay, 14, 0);
        await FillSheetsAsync(round, cancellationToken);
        await ChangeStatusAsync(round, RoundTransition.SendToReview, cancellationToken);

        if (publishHour > 14)
        {
            At(matchDay, publishHour, 0);
        }
        else
        {
            At(matchDay + 1, publishHour, 0);
        }

        await PublishAsync(round, cancellationToken);
    }

    private async Task FillSheetsAsync(int round, CancellationToken cancellationToken)
    {
        await AsOrganizerAsync(async provider =>
        {
            var rounds = provider.GetRequiredService<ICompetitionRoundService>();
            var sheets = provider.GetRequiredService<IMatchSheetService>();
            var current = await CurrentRoundAsync(rounds, round, cancellationToken);
            var matches = current.Matches.OrderBy(match => match.KickoffAt).ToList();
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                var sheet = await sheets.GetAsync(_competitionId, match.Id, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Súmula de {match.HomeTeamName} × {match.AwayTeamName} não abriu.");
                var (home, away) = DemoUniverse.Scores[round][index];
                var saved = await sheets.SaveAsync(
                    _competitionId,
                    match.Id,
                    SheetFactory.Build(sheet, home, away, _random),
                    cancellationToken);
                Ensure(
                    saved.Outcome == MatchSheetCommandOutcome.Completed,
                    $"súmula de {match.HomeTeamName} × {match.AwayTeamName}",
                    saved.Errors.Select(e => e.Message));
            }
        });
    }

    private async Task PublishAsync(int round, CancellationToken cancellationToken)
    {
        await AsOrganizerAsync(async provider =>
        {
            var review = await provider.GetRequiredService<ICompetitionRoundService>()
                .ReviewAsync(_competitionId, _rounds[round], cancellationToken)
                ?? throw new InvalidOperationException($"Conferência da Rodada {round + 1} não abriu.");
            Ensure(review.Ready, $"conferência da Rodada {round + 1}", review.Pending.Select(item => item.ToString()));
            var published = await provider.GetRequiredService<IRoundPublicationService>()
                .PublishAsync(_competitionId, _rounds[round], review.Version, cancellationToken);
            Ensure(
                published.Outcome == RoundPublicationOutcome.Completed,
                $"publicação da Rodada {round + 1}",
                published.Errors.Select(e => e.Message));
        });
    }

    /// <summary>
    /// A correção que a demonstração mostra: um gol lançado no atleta errado. A rodada é
    /// reaberta com o motivo, a súmula muda o autor e a apuração sai de novo.
    /// </summary>
    private async Task CorrectRoundAsync(int round, CancellationToken cancellationToken)
    {
        const string Reason = "Um gol da primeira partida foi lançado no atleta errado.";
        await AsOrganizerAsync(async provider =>
        {
            var review = await provider.GetRequiredService<ICompetitionRoundService>()
                .ReviewAsync(_competitionId, _rounds[round], cancellationToken)
                ?? throw new InvalidOperationException("Conferência não abriu para a correção.");
            var reopened = await provider.GetRequiredService<IRoundPublicationService>()
                .ReopenAsync(_competitionId, _rounds[round], review.Version, Reason, cancellationToken);
            Ensure(
                reopened.Outcome == RoundPublicationOutcome.Completed,
                $"reabertura da Rodada {round + 1}",
                reopened.Errors.Select(e => e.Message));
        });

        At(-7, 10, 20);
        await AsOrganizerAsync(async provider =>
        {
            var rounds = provider.GetRequiredService<ICompetitionRoundService>();
            var sheets = provider.GetRequiredService<IMatchSheetService>();
            var first = (await rounds.ListAsync(_competitionId, cancellationToken))
                .Single(item => item.Id == _rounds[round]).Matches.MinBy(match => match.KickoffAt)!;
            var sheet = await sheets.GetAsync(_competitionId, first.Id, cancellationToken)
                ?? throw new InvalidOperationException("Súmula a corrigir não abriu.");

            // O gol sai de quem marcou e vai para outro jogador de linha do mesmo time.
            var athletes = sheet.Athletes.OrderBy(athlete => athlete.SportingName, StringComparer.Ordinal).ToList();
            var scorer = athletes.First(athlete => athlete.Goals > 0);
            var fixedBy = athletes.First(athlete =>
                athlete.RealTeamId == scorer.RealTeamId
                && athlete.AthleteId != scorer.AthleteId
                && athlete.DidPlay
                && !athlete.PlayedAsGoalkeeper);
            var corrected = new MatchSheetInput(
                sheet.HomeScore,
                sheet.AwayScore,
                [.. sheet.Athletes.Select(athlete => new MatchSheetAppearanceInput(
                    athlete.AthleteId,
                    athlete.DidPlay,
                    athlete.PlayedAsGoalkeeper,
                    athlete.GoalsConceded,
                    athlete.Goals + GoalShift(athlete.AthleteId, scorer.AthleteId, fixedBy.AthleteId),
                    athlete.Assists,
                    athlete.GoalkeeperSaves,
                    athlete.PenaltySaves,
                    athlete.YellowCards,
                    athlete.RedCards,
                    athlete.RedCardReason,
                    athlete.OwnGoals,
                    athlete.PenaltyMisses))],
                sheet.Version);
            var saved = await sheets.SaveAsync(_competitionId, first.Id, corrected, cancellationToken);
            Ensure(
                saved.Outcome == MatchSheetCommandOutcome.Completed,
                "súmula corrigida",
                saved.Errors.Select(e => e.Message));
        });

        At(-7, 10, 40);
        await PublishAsync(round, cancellationToken);
    }

    /// <summary>
    /// Entra no campeonato e compra o elenco: titulares entre os mais caros que o saldo
    /// ainda permite, banco barato, técnico no fim e um capitão de linha. Cada conta faz
    /// escolhas diferentes, então o ranking não termina num empate de doze.
    /// </summary>
    private async Task JoinWithSquadAsync(Guid userId, CancellationToken cancellationToken)
    {
        await AsAsync(userId, [], async provider =>
        {
            var fantasy = provider.GetRequiredService<IFantasyService>();
            var joined = await fantasy.JoinAsync(_slug, cancellationToken);
            Ensure(joined.Outcome == FantasyCommandOutcome.Completed, "adesão ao campeonato");

            // O técnico é a última vaga: posição nula.
            var plan = StarterOrder.Select(position => ((string?)position, starter: true))
                .Concat(BenchOrder.Select(position => ((string?)position, starter: false)))
                .Append((null, starter: false))
                .ToList();
            for (var slot = 0; slot < plan.Count; slot++)
            {
                var (position, starter) = plan[slot];
                var remaining = plan.Skip(slot + 1).Select(next => next.Item1).ToList();
                await BuyAsync(fantasy, position, starter, remaining, cancellationToken);
            }

            var overview = await fantasy.OverviewAsync(_slug, cancellationToken);
            var starters = overview!.Entry!.Slots
                .Where(slot => slot.Role == "Starter" && slot.Kind == "Athlete" && slot.Position != "Goalkeeper")
                .OrderBy(slot => slot.Name, StringComparer.Ordinal)
                .ToList();
            var captain = starters[_random.Next(starters.Count)];
            var set = await fantasy.SetCaptainAsync(_slug, captain.AssetId, cancellationToken);
            Ensure(set.Outcome == FantasyCommandOutcome.Completed, "capitão");
        });
    }

    /// <summary>
    /// Compra um ativo da posição (ou o técnico, com <paramref name="position"/> nulo),
    /// deixando no saldo o bastante para as vagas que ainda faltam: o preço mais baixo à
    /// venda em cada uma delas, lido do próprio mercado.
    /// </summary>
    private async Task BuyAsync(
        IFantasyService fantasy,
        string? position,
        bool starter,
        IReadOnlyList<string?> remaining,
        CancellationToken cancellationToken)
    {
        var market = await fantasy.MarketAsync(_slug, cancellationToken)
            ?? throw new InvalidOperationException("Mercado não abriu.");
        // Ordem pelo nome, e não pela que o banco devolve: os identificadores mudam a cada
        // reset, e o mesmo reset precisa terminar no mesmo campeonato.
        var forSale = market.Items
            .Where(item => item.BlockCode is null && !item.IsOwned)
            .OrderBy(item => item.RealTeamName, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        bool Fits(MarketItemView item, string? slot) =>
            slot is null ? item.Kind == "Coach" : item.Kind == "Athlete" && item.Position == slot;

        var reserve = remaining.Sum(slot => forSale
            .Where(item => Fits(item, slot))
            .Select(item => item.Price)
            .DefaultIfEmpty(0m)
            .Min());
        var budget = (market.Balance ?? 0m) - reserve;
        var candidates = forSale.Where(item => Fits(item, position)).OrderByDescending(item => item.Price).ToList();
        var affordable = candidates.Where(item => item.Price <= budget).ToList();

        // Titular entre os melhores que cabem; reserva entre os mais baratos que cabem. Sem
        // nada dentro do orçamento, só o mais barato de todos.
        var pool = affordable.Count == 0
            ? [.. candidates.OrderBy(item => item.Price).Take(1)]
            : starter
                ? affordable.Take(Math.Max(1, affordable.Count / 2)).ToList()
                : [.. affordable.OrderBy(item => item.Price).Take(3)];
        foreach (var choice in pool.OrderBy(_ => _random.Next()))
        {
            var bought = await fantasy.BuyAsync(
                _slug,
                choice.Kind == "Coach" ? AssetKind.Coach : AssetKind.Athlete,
                choice.Id,
                cancellationToken);
            if (bought.Outcome == FantasyCommandOutcome.Completed)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Nenhum ativo de {position ?? "técnico"} coube no elenco (saldo {market.Balance}, reserva {reserve}).");
    }

    /// <summary>Entre uma rodada e outra, quem joga troca um reserva por outro da posição.</summary>
    private async Task TransfersAsync(IEnumerable<DemoPerson> players, CancellationToken cancellationToken)
    {
        foreach (var player in players)
        {
            var userId = _accounts[player.Email];
            await AsAsync(userId, [], async provider =>
            {
                var fantasy = provider.GetRequiredService<IFantasyService>();
                var overview = await fantasy.OverviewAsync(_slug, cancellationToken);
                var bench = overview!.Entry!.Slots
                    .Where(slot => slot.Role == "Bench" && slot.Kind == "Athlete" && slot.Position != "Goalkeeper")
                    .OrderBy(slot => slot.Name, StringComparer.Ordinal)
                    .ToList();
                var leaving = bench[_random.Next(bench.Count)];
                var sold = await fantasy.SellAsync(_slug, AssetKind.Athlete, leaving.AssetId, cancellationToken);
                Ensure(sold.Outcome == FantasyCommandOutcome.Completed, $"venda de {leaving.Name}");
                await BuyAsync(fantasy, leaving.Position, starter: false, [], cancellationToken);
            });
        }
    }

    private async Task CreateLeaguesAsync(CancellationToken cancellationToken)
    {
        var players = DemoUniverse.Players.Take(DemoUniverse.Players.Count - DemoUniverse.LateJoiners).ToList();
        var viewerCode = await CreateLeagueAsync(_viewerId, DemoUniverse.ViewerLeague, cancellationToken);
        foreach (var player in players.Take(5))
        {
            await JoinLeagueAsync(_accounts[player.Email], viewerCode, cancellationToken);
        }

        var friendsCode = await CreateLeagueAsync(
            _accounts[players[5].Email],
            DemoUniverse.FriendsLeague,
            cancellationToken);
        await JoinLeagueAsync(_viewerId, friendsCode, cancellationToken);
        foreach (var player in players.Skip(6))
        {
            await JoinLeagueAsync(_accounts[player.Email], friendsCode, cancellationToken);
        }
    }

    private async Task<string> CreateLeagueAsync(Guid ownerId, string name, CancellationToken cancellationToken)
    {
        string? code = null;
        await AsAsync(ownerId, [], async provider =>
        {
            var created = await provider.GetRequiredService<ILeagueService>()
                .CreateAsync(_slug, new LeagueDefinition(name), cancellationToken);
            Ensure(
                created.Outcome == LeagueCommandOutcome.Completed,
                $"liga {name}",
                created.Errors.Select(e => e.Message));
            code = created.League!.InviteCode;
        });
        return code ?? throw new InvalidOperationException($"A liga {name} não devolveu código de convite.");
    }

    private async Task JoinLeagueAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        await AsAsync(userId, [], async provider =>
        {
            var joined = await provider.GetRequiredService<ILeagueService>().JoinAsync(code, cancellationToken);
            Ensure(
                joined.Outcome == LeagueCommandOutcome.Completed,
                "entrada na liga",
                joined.Errors.Select(e => e.Message));
        });
    }

    private Task AsOrganizerAsync(Func<IServiceProvider, Task> step) =>
        AsAsync(_organizerId, [ApplicationRole.Organizer], step);

    /// <summary>Um passo num escopo novo, como uma requisição, no nome de <paramref name="userId"/>.</summary>
    private async Task AsAsync(Guid userId, string[] roles, Func<IServiceProvider, Task> step)
    {
        actor.Become(userId, roles);
        await using var scope = services.CreateAsyncScope();
        await step(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>Leva o relógio ao dia <paramref name="day"/> (relativo a hoje), na hora local dada.</summary>
    private void At(int day, int hour, int minute)
    {
        var local = _today.AddDays(day).ToDateTime(new TimeOnly(hour, 0)).AddMinutes(minute);
        clock.AdvanceTo(new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _zone), TimeSpan.Zero));
    }

    private async Task<RoundView> CurrentRoundAsync(
        ICompetitionRoundService rounds,
        int round,
        CancellationToken cancellationToken) =>
        (await rounds.ListAsync(_competitionId, cancellationToken)).Single(item => item.Id == _rounds[round]);

    /// <summary>O gol sai de quem marcou (-1) e vai para quem de fato marcou (+1).</summary>
    private static int GoalShift(Guid athlete, Guid from, Guid to) =>
        athlete == from ? -1 : athlete == to ? 1 : 0;

    private static void Ensure(bool condition, string step, IEnumerable<string>? details = null)
    {
        if (!condition)
        {
            var reasons = details?.ToList() ?? [];
            throw new InvalidOperationException(
                reasons.Count == 0
                    ? $"O seed falhou em: {step}."
                    : $"O seed falhou em: {step}. {string.Join(" ", reasons)}");
        }
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(error => error.Description));

    private static byte[] ReadDemoFile(string file)
    {
        using var stream = typeof(DemoSeeder).Assembly.GetManifestResourceStream($"dados-demo.{file}")
            ?? throw new InvalidOperationException($"Arquivo {file} não foi embutido no seed.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static IEnumerable<string> DemoFileLines(string file)
    {
        using var reader = new StreamReader(
            new MemoryStream(ReadDemoFile(file)),
            detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
