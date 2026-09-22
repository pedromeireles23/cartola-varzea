import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Button, Card } from '../../shared/ui';
import { mensagemDeConvite } from './fantasy-leagues';
import { LEAGUE_CODE_LENGTH, LeagueService, LeagueSummary } from './league.service';

type Estado =
  | { readonly tipo: 'convite' }
  | { readonly tipo: 'dentro'; readonly liga: LeagueSummary }
  | { readonly tipo: 'erro'; readonly mensagem: string };

/**
 * Aceitar um convite de liga pelo link (02 §9.1, `/convite/:codigo`).
 *
 * A tela não entra sozinha ao abrir: entrar é uma escrita, e uma escrita disparada só
 * por abrir um link acontece sem a pessoa decidir. O `authGuard` já trouxe quem não
 * estava logado de volta para cá depois da entrada.
 */
@Component({
  selector: 'app-league-invite',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, RouterLink],
  template: `
    <h1>Convite para uma liga</h1>

    @switch (estado().tipo) {
      @case ('convite') {
        <app-card heading="Você foi convidado">
          <p>
            Alguém compartilhou com você o convite de uma liga privada. Ao entrar, sua pontuação no
            campeonato passa a aparecer também na classificação dessa liga.
          </p>
          <p class="apoio">
            A liga não muda nada no seu jogo: mesmo elenco, mesma pontuação, mesmas rodadas.
          </p>
          <p class="convite__codigo">
            <span class="sr-only">Código do convite:</span>
            <span class="convite__valor">{{ agrupado() }}</span>
          </p>
          @if (!pareceCodigo()) {
            <app-alert tone="warning">
              Este link não parece ter um código completo. Confira se ele foi copiado inteiro.
            </app-alert>
          }
          <app-button [loading]="entrando()" [disabled]="!pareceCodigo()" (pressed)="entrar()">
            Entrar na liga
          </app-button>
        </app-card>
      }
      @case ('dentro') {
        <app-card heading="Tudo certo">
          <app-alert tone="success">Você entrou na liga {{ liga()!.name }}.</app-alert>
          <p class="apoio">
            {{ liga()!.members }} {{ liga()!.members === 1 ? 'participante' : 'participantes' }} na
            liga.
          </p>
          <a class="acao" [routerLink]="['/c', liga()!.competitionSlug, 'ligas', liga()!.id]"
            >Ver a classificação da liga</a
          >
        </app-card>
      }
      @case ('erro') {
        <app-card heading="Não deu para entrar">
          <app-alert tone="danger">{{ mensagem() }}</app-alert>
          <div class="acoes-da-tela">
            <app-button variant="secondary" (pressed)="voltar()">Tentar de novo</app-button>
            <a class="acao acao--secundaria" routerLink="/campeonatos">Ver campeonatos</a>
          </div>
        </app-card>
      }
    }
  `,
  styleUrls: ['./fantasy.scss', './leagues.scss'],
})
export class LeagueInvitePage implements OnInit {
  private readonly service = inject(LeagueService);
  private readonly title = inject(Title);

  /** Código do convite na rota. */
  readonly codigo = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'convite' });
  protected readonly entrando = signal(false);

  ngOnInit(): void {
    this.title.setTitle('Convite para uma liga · Cartola Várzea');
  }

  protected liga(): LeagueSummary | null {
    const atual = this.estado();
    return atual.tipo === 'dentro' ? atual.liga : null;
  }

  protected mensagem(): string {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.mensagem : '';
  }

  /** O servidor tira hífens e maiúsculas antes de comparar; aqui só o tamanho importa. */
  protected pareceCodigo(): boolean {
    return this.limpo().length === LEAGUE_CODE_LENGTH;
  }

  /** Dez caracteres seguidos são difíceis de conferir; em dois blocos de cinco, não. */
  protected agrupado(): string {
    const limpo = this.limpo();
    return limpo.length === LEAGUE_CODE_LENGTH
      ? `${limpo.slice(0, 5)}-${limpo.slice(5)}`
      : this.codigo();
  }

  protected entrar(): void {
    this.entrando.set(true);
    this.service.join(this.codigo()).subscribe({
      next: (liga) => {
        this.entrando.set(false);
        this.estado.set({ tipo: 'dentro', liga });
      },
      error: (falha: ApiFailure) => {
        this.entrando.set(false);
        this.estado.set({ tipo: 'erro', mensagem: mensagemDeConvite(falha) });
      },
    });
  }

  protected voltar(): void {
    this.estado.set({ tipo: 'convite' });
  }

  private limpo(): string {
    return this.codigo().replace(/[^a-z0-9]/gi, '');
  }
}
