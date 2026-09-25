namespace Fut7Fantasy.Demo;

/// <summary>Uma conta da demonstração: nome de exibição e e-mail, nunca a senha.</summary>
internal sealed record DemoPerson(string DisplayName, string Email);

/// <summary>
/// O universo fictício da demonstração (01 §11 e §13). Nenhum nome copia pessoa, clube,
/// escudo ou marca real: são apelidos e sobrenomes comuns, e os times e atletas vêm da
/// massa versionada em <c>infra/dados-demo</c>, a mesma do E2E. Os endereços usam o
/// domínio reservado <c>.test</c>, que nunca recebe e-mail.
/// </summary>
internal static class DemoUniverse
{
    public const string OrganizationName = "Liga Várzea do Leste";
    public const string CompetitionName = "Copa da Vila";
    public const string Season = "2026";
    public const string StageName = "Pontos corridos";
    public const string GroupName = "Grupo único";

    /// <summary>Fuso do regulamento; o seed marca as partidas pelo relógio da cidade.</summary>
    public const string TimeZoneId = "America/Sao_Paulo";

    /// <summary>Quem organiza a Copa. Senha privada, para a demonstração gravada.</summary>
    public static readonly DemoPerson Organizer = new("Marta Ribeiro", "organizacao@demo.cartola-varzea.test");

    /// <summary>A administração da plataforma. Senha privada.</summary>
    public static readonly DemoPerson Admin = new("Administração da demo", "admin@demo.cartola-varzea.test");

    /// <summary>
    /// A conta pública de visitante (<c>DemoViewer</c>): joga a Copa, está em duas ligas e
    /// auxilia a organização — vê tudo, e o servidor recusa qualquer escrita.
    /// </summary>
    public static readonly DemoPerson Viewer = new("Visitante da demo", "visitante@demo.cartola-varzea.test");

    /// <summary>
    /// Quem joga a Copa além do visitante. Não têm senha: existem para o ranking e as
    /// ligas terem gente de verdade dentro, e ninguém entra por eles.
    /// </summary>
    public static readonly IReadOnlyList<DemoPerson> Players =
    [
        new("Rafa Souza", "rafa.souza@jogadores.demo.cartola-varzea.test"),
        new("Bia Lima", "bia.lima@jogadores.demo.cartola-varzea.test"),
        new("Caio Mendes", "caio.mendes@jogadores.demo.cartola-varzea.test"),
        new("Duda Rocha", "duda.rocha@jogadores.demo.cartola-varzea.test"),
        new("Léo Martins", "leo.martins@jogadores.demo.cartola-varzea.test"),
        new("Nina Alves", "nina.alves@jogadores.demo.cartola-varzea.test"),
        new("Tião Ferreira", "tiao.ferreira@jogadores.demo.cartola-varzea.test"),
        new("Juca Prado", "juca.prado@jogadores.demo.cartola-varzea.test"),
        new("Lia Campos", "lia.campos@jogadores.demo.cartola-varzea.test"),
        new("Beto Nunes", "beto.nunes@jogadores.demo.cartola-varzea.test"),
        new("Gabi Teles", "gabi.teles@jogadores.demo.cartola-varzea.test"),
        new("Zé Moreira", "ze.moreira@jogadores.demo.cartola-varzea.test"),
    ];

    /// <summary>Quantos jogadores entram só a partir da Rodada 3, para o ranking ter atrasados.</summary>
    public const int LateJoiners = 2;

    /// <summary>
    /// Pedidos de acesso que ficam na fila da administração, para a demonstração da
    /// aprovação mostrar algo por decidir.
    /// </summary>
    public static readonly IReadOnlyList<(DemoPerson Person, string Organization)> PendingApplications =
    [
        (new DemoPerson("Toninho Barros", "toninho.barros@pedidos.demo.cartola-varzea.test"), "Liga do Morro Alto"),
        (new DemoPerson("Cida Freitas", "cida.freitas@pedidos.demo.cartola-varzea.test"), "Copa Beira-Rio"),
    ];

    /// <summary>A liga que o visitante criou, e quem entrou pelo código dele.</summary>
    public const string ViewerLeague = "Liga da Firma";

    /// <summary>A liga em que o visitante entrou pelo convite de outra pessoa.</summary>
    public const string FriendsLeague = "Resenha de Domingo";

    /// <summary>
    /// Placar de cada partida do turno, rodada a rodada, na ordem do calendário. Escritos
    /// à mão para a tabela ter vitória, empate, goleada e zero a zero.
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<(int Home, int Away)>> Scores =
    [
        [(3, 1), (2, 2), (0, 1)],
        [(1, 0), (4, 2), (1, 1)],
        [(2, 3), (0, 0), (5, 1)],
        [(1, 2), (3, 3), (2, 0)],
    ];

    /// <summary>
    /// O turno de seis times em cinco rodadas, pelo índice do time na massa: cada time
    /// joga uma vez por rodada e enfrenta todos os outros uma vez (método do círculo).
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<(int Home, int Away)>> Fixtures =
    [
        [(0, 5), (1, 4), (2, 3)],
        [(5, 3), (4, 2), (0, 1)],
        [(1, 5), (2, 0), (3, 4)],
        [(5, 4), (0, 3), (1, 2)],
        [(2, 5), (3, 1), (4, 0)],
    ];

    /// <summary>Horário local dos três jogos de uma rodada.</summary>
    public static readonly IReadOnlyList<TimeOnly> KickoffTimes =
    [
        new(9, 0), new(10, 30), new(12, 0),
    ];
}
