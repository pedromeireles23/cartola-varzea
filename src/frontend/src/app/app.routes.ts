import { Routes } from '@angular/router';

import { anonymousGuard, authGuard } from './core/auth/auth.guard';
import { PublicLayout } from './layouts/public-layout/public-layout';

/**
 * Rotas em pt-BR porque aparecem para o usuario (02 §9.1). As areas do
 * organizador e do admin entram nas fases seguintes, com lazy loading por area.
 */
export const routes: Routes = [
  {
    path: '',
    component: PublicLayout,
    children: [
      {
        // A raiz é a porta de entrada do produto (Fase 8); antes ela redirecionava para
        // a página de sistema, que é ferramenta de diagnóstico, não proposta de valor.
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./features/public/landing').then((m) => m.LandingPage),
      },
      {
        path: 'sistema',
        title: 'Estado do sistema',
        loadComponent: () =>
          import('./features/system-info/system-info').then((m) => m.SystemInfoPage),
      },
      {
        path: 'campeonatos',
        title: 'Campeonatos',
        loadComponent: () =>
          import('./features/public/competition-search').then((m) => m.CompetitionSearchPage),
      },
      {
        // O identificador público é o slug, dado na publicação e estável desde então
        // (decidido em 2026-09-17); o GUID continua valendo só na área de organização.
        path: 'c/:campeonato',
        loadComponent: () =>
          import('./features/public/public-competition').then((m) => m.PublicCompetitionPage),
      },
      {
        // O ranking é conteúdo público (01 §10): quem não tem conta vê a classificação.
        path: 'c/:campeonato/ranking',
        title: 'Ranking',
        loadComponent: () =>
          import('./features/public/competition-ranking').then((m) => m.CompetitionRankingPage),
      },
      {
        // Calendário e súmula são públicos: a súmula só existe depois que a rodada
        // publica, e o servidor responde 404 antes disso.
        path: 'c/:campeonato/partidas',
        title: 'Partidas',
        loadComponent: () =>
          import('./features/public/competition-fixtures').then((m) => m.CompetitionFixturesPage),
      },
      {
        path: 'c/:campeonato/partidas/:partidaId',
        title: 'Súmula',
        loadComponent: () =>
          import('./features/public/public-match').then((m) => m.PublicMatchPage),
      },
      {
        // O jogo usa o mesmo slug da página pública; exige conta, e o destino é preservado.
        path: 'c/:campeonato/jogar',
        title: 'Jogar',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/fantasy/fantasy-play').then((m) => m.FantasyPlayPage),
      },
      {
        path: 'c/:campeonato/mercado',
        title: 'Mercado',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/fantasy/fantasy-market').then((m) => m.FantasyMarketPage),
      },
      {
        path: 'c/:campeonato/pontuacao/:rodada',
        title: 'Pontuação da rodada',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/fantasy/fantasy-score').then((m) => m.FantasyScorePage),
      },
      {
        path: 'c/:campeonato/escalacao',
        title: 'Escalação',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/fantasy/fantasy-lineup').then((m) => m.FantasyLineupPage),
      },
      {
        path: 'c/:campeonato/ligas',
        title: 'Ligas',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/fantasy/fantasy-leagues').then((m) => m.FantasyLeaguesPage),
      },
      {
        // A liga fica sob o campeonato porque a API e a navegação do jogo precisam do
        // slug; o identificador da liga continua sendo o GUID, que não é adivinhável.
        path: 'c/:campeonato/ligas/:ligaId',
        title: 'Liga',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/fantasy/fantasy-league').then((m) => m.FantasyLeaguePage),
      },
      {
        // Quem recebe um convite ainda não sabe de que campeonato ele é, então esta é a
        // única rota de liga fora do slug. Exige conta, e o destino é preservado.
        path: 'convite/:codigo',
        title: 'Convite para uma liga',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/fantasy/league-invite').then((m) => m.LeagueInvitePage),
      },
      {
        path: 'entrar',
        title: 'Entrar',
        canActivate: [anonymousGuard],
        loadComponent: () => import('./features/auth/login').then((m) => m.LoginPage),
      },
      {
        path: 'cadastro',
        title: 'Criar conta',
        canActivate: [anonymousGuard],
        loadComponent: () => import('./features/auth/register').then((m) => m.RegisterPage),
      },
      {
        path: 'verificar-email',
        title: 'Confirmação de e-mail',
        loadComponent: () => import('./features/auth/verify-email').then((m) => m.VerifyEmailPage),
      },
      {
        path: 'recuperar-senha',
        title: 'Recuperar senha',
        loadComponent: () =>
          import('./features/auth/password-recovery').then((m) => m.PasswordRecoveryPage),
      },
      {
        path: 'perfil',
        title: 'Seu perfil',
        canActivate: [authGuard],
        loadComponent: () => import('./features/profile/profile').then((m) => m.ProfilePage),
      },
      {
        // As telas de organização usam a casca pública, como o perfil. Dentro de um
        // campeonato, a casca da área (CompetitionLayout) acrescenta a navegação lateral.
        path: 'organizar',
        pathMatch: 'full',
        title: 'Minhas organizações',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/organizer/my-organizations').then((m) => m.MyOrganizationsPage),
      },
      {
        // Sem guard de login: a tela lê e retira o token da URL antes de pedir a entrada.
        path: 'organizar/convite',
        title: 'Convite para auxiliar',
        loadComponent: () =>
          import('./features/organizer/accept-invitation').then((m) => m.AcceptInvitationPage),
      },
      {
        path: 'organizar/o/:organizacao/equipe',
        title: 'Equipe da organização',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/organizer/organization-team').then((m) => m.OrganizationTeamPage),
      },
      {
        path: 'organizar/o/:organizacao/campeonatos',
        title: 'Campeonatos da organização',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/organizer/organization-competitions').then(
            (m) => m.OrganizationCompetitionsPage,
          ),
      },
      {
        path: 'organizar/o/:organizacao/campeonatos/novo',
        title: 'Novo campeonato',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/organizer/create-competition').then((m) => m.CreateCompetitionPage),
      },
      {
        // Na área de organização o identificador é o GUID; a URL pública usa o slug
        // dado na publicação, que não muda quando o campeonato é renomeado.
        path: 'organizar/c/:campeonato',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/organizer/competition-area/competition-layout').then(
            (m) => m.CompetitionLayout,
          ),
        children: [
          {
            path: '',
            pathMatch: 'full',
            title: 'Resumo',
            loadComponent: () =>
              import('./features/organizer/competition-area/competition-summary').then(
                (m) => m.CompetitionSummaryPage,
              ),
          },
          {
            path: 'fases',
            title: 'Fases',
            loadComponent: () =>
              import('./features/organizer/competition-area/competition-stages').then(
                (m) => m.CompetitionStagesPage,
              ),
          },
          {
            path: 'rodadas',
            title: 'Rodadas',
            loadComponent: () =>
              import('./features/organizer/competition-area/competition-rounds').then(
                (m) => m.CompetitionRoundsPage,
              ),
          },
          {
            path: 'rodadas/:rodada/revisao',
            title: 'Revisão da rodada',
            loadComponent: () =>
              import('./features/organizer/competition-area/round-review').then(
                (m) => m.RoundReviewPage,
              ),
          },
          {
            path: 'partidas/:partida/sumula',
            title: 'Súmula da partida',
            loadComponent: () =>
              import('./features/organizer/competition-area/match-sheet').then(
                (m) => m.MatchSheetPage,
              ),
          },
          {
            path: 'times',
            title: 'Times',
            loadComponent: () =>
              import('./features/organizer/competition-area/competition-teams').then(
                (m) => m.CompetitionTeamsPage,
              ),
          },
          {
            path: 'atletas',
            title: 'Atletas',
            loadComponent: () =>
              import('./features/organizer/competition-area/competition-athletes').then(
                (m) => m.CompetitionAthletesPage,
              ),
          },
          {
            path: 'tecnicos',
            title: 'Técnicos',
            loadComponent: () =>
              import('./features/organizer/competition-area/competition-coaches').then(
                (m) => m.CompetitionCoachesPage,
              ),
          },
          {
            path: 'importacoes',
            title: 'Importações',
            loadComponent: () =>
              import('./features/organizer/competition-area/competition-imports').then(
                (m) => m.CompetitionImportsPage,
              ),
          },
          {
            path: 'publicacao',
            title: 'Publicação',
            loadComponent: () =>
              import('./features/organizer/competition-area/competition-publication').then(
                (m) => m.CompetitionPublicationPage,
              ),
          },
          {
            path: 'configuracao',
            title: 'Configuração',
            loadComponent: () =>
              import('./features/organizer/competition-area/competition-settings').then(
                (m) => m.CompetitionSettingsPage,
              ),
          },
        ],
      },
      {
        path: 'organizar/solicitar',
        title: 'Organizar campeonatos',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/organizer/request-access').then((m) => m.RequestAccessPage),
      },
      {
        // Só exige login: quem não é Platform admin recebe 403 da API e a tela mostra o
        // estado sem permissão. A navegação própria da administração chega depois.
        path: 'admin/solicitacoes',
        title: 'Solicitações de organizador',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/platform-admin/organizer-applications').then(
            (m) => m.OrganizerApplicationsPage,
          ),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
