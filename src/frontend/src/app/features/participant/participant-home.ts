import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ArrowRight, CircleAlert, CircleCheck, Clock, Search, Ticket, Trophy } from 'lucide';

import { AuthService } from '../../core/auth/auth.service';
import { Alert, Badge, Button, Icon, Loading, PageHeader } from '../../shared/ui';
import {
  closingText,
  countdownText,
  credits,
  kickoffText,
  points,
} from '../fantasy/fantasy-format';
import {
  FantasyOverview,
  FantasyRoundSummary,
  FantasyService,
  MyFantasyCompetition,
} from '../fantasy/fantasy.service';
import { formacaoCurta } from '../fantasy/lineup-model';
import { MODALITY_LABELS, Modality } from '../organizer/competition.service';
import {
  PublicCompetitionService,
  PublicCompetitionSummary,
  Ranking,
} from '../public/public-competition.service';
import { PublicFixture, PublicFixtureService } from '../public/public-fixture.service';
import { CurrentCompetition } from './current-competition';

type Painel =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly visao: FantasyOverview }
  | { readonly tipo: 'erro' };

/** Quantas linhas da classificação aparecem no início, além da linha de quem lê. */
const LINHAS_DO_TOPO = 2;

/**
 * Início da pessoa autenticada (06 §5 e §10).
 *
 * Com campeonato, é o painel da rodada, na ordem do que decide a próxima ação: o prazo
 * absoluto do mercado, a escalação com o que falta e o orçamento, a posição no
 * campeonato, as próximas partidas e os avisos de apuração. Nenhum número é inventado:
 * o que o servidor ainda não tem vira uma frase dizendo quando vai ter.
 *
 * Sem campeonato, a conta nova não cai num vazio: a tela diz que a conta está pronta,
 * mostra campeonatos abertos, a busca e o caminho do convite de liga.
 */
@Component({
  selector: 'app-participant-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Icon, Loading, PageHeader, RouterLink],
  templateUrl: './participant-home.html',
  styleUrls: ['./participant-home.scss', './participant-home-first.scss'],
})
export class ParticipantHomePage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fantasy = inject(FantasyService);
  private readonly publicCompetitions = inject(PublicCompetitionService);
  private readonly fixtures = inject(PublicFixtureService);
  protected readonly competitions = inject(CurrentCompetition);

  protected readonly icons = {
    clock: Clock,
    arrow: ArrowRight,
    check: CircleCheck,
    pending: CircleAlert,
    search: Search,
    ticket: Ticket,
    trophy: Trophy,
  } as const;

  protected readonly painel = signal<Painel>({ tipo: 'carregando' });
  protected readonly ranking = signal<Ranking | null>(null);
  protected readonly jogos = signal<readonly PublicFixture[] | null>(null);
  protected readonly ultimaRodada = signal<FantasyRoundSummary | null>(null);
  protected readonly abertos = signal<readonly PublicCompetitionSummary[] | null>(null);
  protected readonly busca = signal('');
  protected readonly convite = signal('');

  /** Relógio de minuto em minuto: a contagem regressiva é complemento, não protagonista. */
  private readonly agora = signal(Date.now());

  protected readonly atual = this.competitions.current;
  protected readonly outros = computed(() =>
    this.competitions.mine().filter((item) => item.slug !== this.atual()?.slug),
  );

  protected readonly saudacao = computed(() => {
    const nome = this.auth.current()?.displayName.trim().split(/\s+/)[0] || 'jogador';
    const hora = new Date(this.agora()).getHours();
    const periodo =
      hora >= 5 && hora < 12 ? 'Bom dia' : hora >= 12 && hora < 18 ? 'Boa tarde' : 'Boa noite';
    return `${periodo}, ${nome}`;
  });

  protected readonly visao = computed(() => {
    const atual = this.painel();
    return atual.tipo === 'pronto' ? atual.visao : null;
  });

  protected readonly mercado = computed(() => this.visao()?.market ?? null);

  protected readonly fechamento = computed(() => {
    const mercado = this.mercado();
    return mercado?.closesAtLocal ? closingText(mercado.closesAtLocal, mercado.timeZoneId) : null;
  });

  protected readonly faltam = computed(() => {
    const closesAt = this.mercado()?.closesAt;
    return closesAt ? countdownText(closesAt, this.agora()) : null;
  });

  protected readonly escolhidos = computed(() => this.visao()?.entry?.slots.length ?? 0);

  protected readonly vagas = computed(() => {
    const perfil = this.visao()?.profile;
    return perfil ? perfil.squadAthletes + 1 : 0;
  });

  protected readonly progresso = computed(() =>
    this.vagas() === 0 ? 0 : Math.round((this.escolhidos() / this.vagas()) * 100),
  );

  protected readonly completa = computed(() => this.visao()?.entry?.issues.length === 0);

  protected readonly capitao = computed(
    () => this.visao()?.entry?.slots.find((slot) => slot.isCaptain)?.name ?? null,
  );

  protected readonly formacao = computed(() => {
    const perfil = this.visao()?.profile;
    return perfil ? formacaoCurta(perfil) : '';
  });

  /** "Montar time" com o elenco vazio; "Continuar escalação" com ele pela metade. */
  protected readonly chamada = computed(() =>
    this.escolhidos() === 0 ? 'Montar time' : 'Continuar escalação',
  );

  protected readonly minhaLinha = computed(
    () => this.ranking()?.entries.find((linha) => linha.isViewer) ?? null,
  );

  protected readonly lider = computed(() => this.ranking()?.entries[0] ?? null);

  /** O topo da tabela e a linha de quem lê, sem repetir quem já está no topo. */
  protected readonly linhasDoRanking = computed(() => {
    const linhas = this.ranking()?.entries ?? [];
    const topo = linhas.slice(0, LINHAS_DO_TOPO);
    const minha = this.minhaLinha();
    return minha && !topo.includes(minha) ? [...topo, minha] : topo;
  });

  protected readonly proximosJogos = computed(() => {
    const agora = this.agora();
    return (this.jogos() ?? [])
      .filter((jogo) => jogo.status === 'Scheduled' && Date.parse(jogo.kickoffAt) > agora)
      .sort((um, outro) => Date.parse(um.kickoffAt) - Date.parse(outro.kickoffAt))
      .slice(0, 3);
  });

  /** A rodada apurada mais recente, quando ainda pode mudar: é o aviso que importa. */
  protected readonly avisoDeApuracao = computed(() => {
    const rodada = this.ultimaRodada();
    return rodada && (rodada.provisional || rodada.underCorrection) ? rodada : null;
  });

  constructor() {
    const timer = setInterval(() => this.agora.set(Date.now()), 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));

    // O painel acompanha o campeonato atual: trocar no seletor recarrega o início.
    effect(() => {
      const slug = this.atual()?.slug;
      if (slug) {
        untracked(() => this.carregar(slug));
      }
    });

    // Conta sem campeonato: os abertos aparecem como primeiro passo.
    effect(() => {
      if (this.competitions.loaded() && this.competitions.mine().length === 0) {
        untracked(() => this.carregarAbertos());
      }
    });
  }

  protected carregar(slug: string): void {
    this.painel.set({ tipo: 'carregando' });
    this.ranking.set(null);
    this.jogos.set(null);
    this.ultimaRodada.set(null);

    this.fantasy.overview(slug).subscribe({
      next: (visao) => this.painel.set({ tipo: 'pronto', visao }),
      error: () => this.painel.set({ tipo: 'erro' }),
    });
    // Classificação, partidas e apuração são complementos: se falharem, o bloco some e
    // o resto do painel continua de pé.
    this.publicCompetitions.ranking(slug).subscribe({
      next: (ranking) => this.ranking.set(ranking),
      error: () => this.ranking.set(null),
    });
    this.fixtures.fixtures(slug).subscribe({
      next: (calendario) => this.jogos.set(calendario.rounds.flatMap((rodada) => rodada.matches)),
      error: () => this.jogos.set([]),
    });
    this.fantasy.rounds(slug).subscribe({
      next: (rodadas) => this.ultimaRodada.set(rodadas[0] ?? null),
      error: () => this.ultimaRodada.set(null),
    });
  }

  protected tentarDeNovo(): void {
    const slug = this.atual()?.slug;
    if (slug) {
      this.carregar(slug);
    } else {
      this.competitions.reload();
    }
  }

  protected async trocar(slug: string): Promise<void> {
    await this.competitions.switchTo(slug);
  }

  protected buscar(evento: Event): void {
    evento.preventDefault();
    const termo = this.busca().trim();
    void this.router.navigate(['/campeonatos'], termo ? { queryParams: { busca: termo } } : {});
  }

  protected usarConvite(evento: Event): void {
    evento.preventDefault();
    const codigo = this.convite().trim();
    if (codigo) {
      void this.router.navigate(['/convite', codigo]);
    }
  }

  protected valor(evento: Event): string {
    return (evento.target as HTMLInputElement).value;
  }

  protected creditos(valor: number): string {
    return credits(valor);
  }

  protected pontos(valor: number): string {
    return points(valor);
  }

  protected consolida(rodada: FantasyRoundSummary): string {
    return closingText(rodada.consolidatesAtLocal, rodada.timeZoneId);
  }

  protected quando(jogo: PublicFixture): string {
    return kickoffText(jogo.kickoffLocal);
  }

  protected modalidade(valor: string): string {
    return MODALITY_LABELS[valor as Modality] ?? valor;
  }

  protected elenco(item: MyFantasyCompetition): string {
    return `${item.squadSize} de ${item.squadSizeTarget} no elenco`;
  }

  private carregarAbertos(): void {
    this.publicCompetitions.search('').subscribe({
      next: (itens) => this.abertos.set(itens.slice(0, 5)),
      error: () => this.abertos.set([]),
    });
  }
}
