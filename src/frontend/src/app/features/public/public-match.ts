import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { PageMetaService } from '../../core/seo/page-meta';
import { Alert, BackLink, Button, Card, Loading } from '../../shared/ui';
import { PublicNav } from './public-nav';
import { PublicMatch, PublicMatchAthlete, PublicFixtureService } from './public-fixture.service';
import { kickoffText } from '../fantasy/fantasy-format';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly partida: PublicMatch }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** Posição em pt-BR, abreviada como no campo de escalação (02 §8). */
const POSICOES: Readonly<Record<string, string>> = {
  Goalkeeper: 'Goleiro',
  Defender: 'Defensor',
  Midfielder: 'Meio-campista',
  Forward: 'Atacante',
};

/**
 * Súmula publicada de uma partida (02 §9.1, `/c/:campeonato/partidas/:partida`).
 *
 * Só existe depois que a rodada publica: antes disso o servidor responde como se a
 * partida não existisse, de propósito, porque a diferença entre 404 e 403 contaria que
 * o jogo já tem súmula lançada. Aqui aparece apenas o que o 01 §11 considera público de
 * um atleta — nome esportivo, posição e o que ele fez em campo.
 */
@Component({
  selector: 'app-public-match',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, BackLink, Button, Card, Loading, PublicNav, RouterLink],
  template: `
    <app-back-link [link]="['/c', campeonato(), 'partidas']" label="Todas as partidas" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Súmula</h1>
        <app-card><app-loading label="Abrindo a súmula…" /></app-card>
      }
      @case ('erro') {
        <h1>Súmula</h1>
        <app-card>
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Esta súmula ainda não está disponível. Ela é publicada junto com o resultado da
              rodada.
            </app-alert>
            <a class="acao" [routerLink]="['/c', campeonato(), 'partidas']">Ver o calendário</a>
          } @else {
            <app-alert tone="danger">{{ falha()!.message }}</app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }
      @case ('pronto') {
        <h1>{{ partida()!.homeTeamName }} × {{ partida()!.awayTeamName }}</h1>
        <app-public-nav [campeonato]="campeonato()" atual="partidas" />
        <p class="apoio">
          {{ partida()!.roundName }} · {{ partida()!.stageName }} ·
          {{ quando(partida()!.kickoffLocal) }}
        </p>

        @if (partida()!.provisional) {
          <app-alert tone="warning">
            Resultado provisório: ele ainda pode mudar se a liga corrigir a súmula.
          </app-alert>
        }

        <app-card>
          <div class="sumula__placar">
            <h2 class="sumula__time">{{ partida()!.homeTeamName }}</h2>
            <strong class="sumula__numeros">
              {{ partida()!.homeScore }}<span aria-hidden="true">×</span>{{ partida()!.awayScore }}
              <span class="sr-only">a</span>
            </strong>
            <h2 class="sumula__time">{{ partida()!.awayTeamName }}</h2>
          </div>
        </app-card>

        @for (time of partida()!.teams; track time.name) {
          <app-card [heading]="time.name">
            <ul class="elenco">
              @for (atleta of time.athletes; track atleta.sportingName) {
                <li class="elenco__atleta">
                  <span class="elenco__nome">{{ atleta.sportingName }}</span>
                  <span class="elenco__posicao">{{ posicao(atleta) }}</span>
                  @if (feitos(atleta); as resumo) {
                    <span class="elenco__feitos">{{ resumo }}</span>
                  }
                </li>
              } @empty {
                <li class="apoio">A súmula não registrou quem entrou em campo por este time.</li>
              }
            </ul>
          </app-card>
        }
      }
    }
  `,
  styleUrls: ['./public.scss', './fixtures.scss'],
})
export class PublicMatchPage implements OnInit {
  private readonly service = inject(PublicFixtureService);
  private readonly meta = inject(PageMetaService);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  /** Identificador da partida na rota. */
  readonly partidaId = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  protected readonly partida = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.partida : null;
  });

  /** "ter., 29/09 · 09:00": o horário já vem no fuso do campeonato. */
  protected quando(kickoffLocal: string): string {
    return kickoffText(kickoffLocal);
  }

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected posicao(atleta: PublicMatchAthlete): string {
    const nome = POSICOES[atleta.position] ?? atleta.position;
    // Quem jogou na linha e foi para o gol não é goleiro de posição; a súmula diz os dois.
    return atleta.playedAsGoalkeeper && atleta.position !== 'Goalkeeper' ? `${nome}, no gol` : nome;
  }

  /**
   * O que a pessoa fez, em texto. Só o que aconteceu entra: uma lista de zeros diria
   * menos que o silêncio, e quem só entrou em campo fica com nome e posição.
   */
  protected feitos(atleta: PublicMatchAthlete): string {
    const partes: string[] = [];
    const some = (quantidade: number, um: string, varios: string) => {
      if (quantidade > 0) {
        partes.push(quantidade === 1 ? `1 ${um}` : `${quantidade} ${varios}`);
      }
    };

    some(atleta.goals, 'gol', 'gols');
    some(atleta.assists, 'assistência', 'assistências');
    some(atleta.goalkeeperSaves, 'defesa', 'defesas');
    some(atleta.penaltySaves, 'pênalti defendido', 'pênaltis defendidos');
    some(atleta.penaltyMisses, 'pênalti perdido', 'pênaltis perdidos');
    some(atleta.ownGoals, 'gol contra', 'gols contra');
    some(atleta.yellowCards, 'cartão amarelo', 'cartões amarelos');
    some(atleta.redCards, 'cartão vermelho', 'cartões vermelhos');

    if (atleta.playedAsGoalkeeper && atleta.goalsConceded === 0) {
      partes.push('não sofreu gol');
    }

    return partes.join(' · ');
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.match(this.campeonato(), this.partidaId()).subscribe({
      next: (partida) => {
        this.estado.set({ tipo: 'pronto', partida });
        this.meta.set({
          title: `${partida.homeTeamName} ${partida.homeScore} × ${partida.awayScore} ${partida.awayTeamName}`,
          description: `Súmula de ${partida.homeTeamName} contra ${partida.awayTeamName} pela ${partida.roundName} de ${partida.competitionName}.`,
          type: 'article',
        });
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}
