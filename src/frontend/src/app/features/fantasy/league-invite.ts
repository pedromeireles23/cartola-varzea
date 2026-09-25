import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { switchMap, throwError } from 'rxjs';

import { ApiFailure } from '../../core/api/problem-details';
import { AuthService } from '../../core/auth/auth.service';
import { Alert, Button, Card, Loading } from '../../shared/ui';
import { mensagemDeConvite } from './fantasy-leagues';
import { LEAGUE_CODE_LENGTH, LeagueService, LeagueSummary } from './league.service';

type Estado =
  | { readonly tipo: 'entrando' }
  | { readonly tipo: 'convite' }
  | { readonly tipo: 'dentro'; readonly liga: LeagueSummary }
  | { readonly tipo: 'erro'; readonly mensagem: string };

/**
 * Aceitar um convite de liga pelo link (02 §9.1, `/convite/:codigo`).
 *
 * A tela entra sozinha ao abrir — decisão do Pedro em 2026-09-22, por um fluxo sem
 * atrito. Entrar continua sendo uma escrita disparada por uma navegação, então ela vem
 * cercada:
 *
 * - **só entra.** Nenhuma ação irreversível — remover, apagar, trocar código — acontece
 *   sozinha em lugar nenhum do produto;
 * - **é reversível na mesma tela**: quem caiu aqui sem querer sai num toque, e a saída
 *   fica ao lado da confirmação, não escondida dentro da liga;
 * - **diz o que fez**, com o nome da liga, em vez de deixar a pessoa descobrir depois;
 * - **não gasta tentativa à toa**: código de tamanho errado nem chega ao servidor, o que
 *   preserva o limite de 10 por conta a cada 5 minutos;
 * - **não repete sozinha**: depois de uma recusa, tentar de novo é decisão de quem está
 *   ali, senão um 429 viraria um laço.
 *
 * O `authGuard` da rota garante a sessão antes de a tela montar, então na prática quem
 * chega aqui está sempre logado; a conferência continua explícita para o dia em que a
 * rota trocar de guarda.
 */
@Component({
  selector: 'app-league-invite',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, Loading, RouterLink],
  template: `
    <h1>Convite para uma liga</h1>

    @switch (estado().tipo) {
      @case ('entrando') {
        <app-card><app-loading label="Entrando na liga…" /></app-card>
      }
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
          <app-button
            escrita
            [loading]="entrando()"
            [disabled]="!pareceCodigo()"
            (pressed)="entrar()"
          >
            Entrar na liga
          </app-button>
        </app-card>
      }
      @case ('dentro') {
        <app-card heading="Você entrou na liga">
          <app-alert tone="success">Agora você disputa a {{ liga()!.name }}.</app-alert>
          <p class="apoio">
            {{ liga()!.members }} {{ liga()!.members === 1 ? 'participante' : 'participantes' }} na
            liga. Sua pontuação no campeonato continua a mesma.
          </p>
          <a class="acao" [routerLink]="['/c', liga()!.competitionSlug, 'ligas', liga()!.id]"
            >Ver a classificação da liga</a
          >
          <p class="apoio">Não era o que você queria?</p>
          <app-button escrita variant="ghost" [loading]="saindo()" (pressed)="sair()">
            Sair desta liga
          </app-button>
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
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly title = inject(Title);

  /** Código do convite na rota. */
  readonly codigo = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'convite' });
  protected readonly entrando = signal(false);
  protected readonly saindo = signal(false);

  ngOnInit(): void {
    this.title.setTitle('Convite para uma liga · Cartola Várzea');

    if (this.pareceCodigo() && this.auth.isAuthenticated()) {
      this.estado.set({ tipo: 'entrando' });
      this.entrar();
    }
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

  /**
   * A volta atrás de quem entrou sem querer. O resumo do convite não traz o
   * `membershipId` — ele só vem na liga aberta —, então a saída passa por lá antes.
   */
  protected sair(): void {
    const liga = this.liga();
    if (!liga) {
      return;
    }

    this.saindo.set(true);
    this.service
      .get(liga.competitionSlug, liga.id)
      .pipe(
        switchMap((completa) => {
          const minha = completa.members.find((membro) => membro.isViewer)?.membershipId;
          return minha
            ? this.service.removeMember(liga.competitionSlug, liga.id, minha)
            : throwError(
                () =>
                  ({
                    message: 'Não encontramos a sua participação nesta liga.',
                    status: 0,
                  }) as ApiFailure,
              );
        }),
      )
      .subscribe({
        next: () => {
          this.saindo.set(false);
          // Sair e ficar no convite faria a entrada automática disparar de novo.
          void this.router.navigate(['/c', liga.competitionSlug, 'ligas']);
        },
        error: (falha: ApiFailure) => {
          this.saindo.set(false);
          this.estado.set({ tipo: 'erro', mensagem: falha.message });
        },
      });
  }

  /** Tentar de novo é decisão de quem está aqui: um 429 repetido sozinho viraria laço. */
  protected voltar(): void {
    this.estado.set({ tipo: 'convite' });
  }

  private limpo(): string {
    return this.codigo().replace(/[^a-z0-9]/gi, '');
  }
}
