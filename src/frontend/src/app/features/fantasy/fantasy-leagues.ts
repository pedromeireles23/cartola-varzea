import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { ChevronRight } from 'lucide';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Button, Card, FormField, Icon, Loading } from '../../shared/ui';
import {
  LEAGUE_CODE_LENGTH,
  LEAGUE_LIMIT_CODE,
  LEAGUE_NAME_MAX,
  LEAGUE_NAME_MIN,
  LeagueService,
  LeagueSummary,
} from './league.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly ligas: readonly LeagueSummary[] }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** Só um painel fica aberto por vez: são dois caminhos alternativos, não duas etapas. */
type Painel = 'nenhum' | 'criar' | 'entrar';

/**
 * As ligas da conta num campeonato (02 §9.1, `/c/:campeonato/ligas`).
 *
 * A liga não muda regra nenhuma: ela recorta o mesmo ranking entre quem se conhece. A
 * tela diz isso antes da lista, porque a primeira dúvida de quem cria uma liga é se a
 * pontuação passa a ser outra.
 */
@Component({
  selector: 'app-fantasy-leagues',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, FormField, Icon, Loading, RouterLink],
  template: `
    <p class="intro"><a [routerLink]="['/c', campeonato()]">← Página do campeonato</a></p>

    <h1>Ligas</h1>

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card><app-loading label="Abrindo as suas ligas…" /></app-card>
      }
      @case ('erro') {
        <app-card>
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Não encontramos este campeonato. Ele pode ter saído do ar ou o endereço estar errado.
            </app-alert>
            <a class="acao" routerLink="/campeonatos">Ver campeonatos publicados</a>
          } @else {
            <app-alert tone="danger">{{ falha()!.message }}</app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }
      @case ('pronto') {
        <p class="apoio">
          Uma liga privada é um recorte do ranking entre pessoas que você conhece. A pontuação é a
          mesma do campeonato: ninguém joga com regra diferente por estar numa liga.
        </p>

        @if (ligas().length === 0) {
          <app-card heading="Você ainda não está em nenhuma liga">
            <p>
              Crie a sua e compartilhe o código com o grupo, ou entre na liga de alguém com o código
              que você recebeu.
            </p>
          </app-card>
        } @else {
          <ul class="cartoes-link">
            @for (liga of ligas(); track liga.id) {
              <li class="cartao-link" [style.--indice]="$index">
                <h2 class="cartao-link__titulo">
                  <a [routerLink]="['/c', campeonato(), 'ligas', liga.id]">{{ liga.name }}</a>
                </h2>
                <p class="cartao-link__detalhe">
                  {{ participantes(liga.members) }}
                  @if (liga.position !== null) {
                    · você está em {{ liga.position }}º
                  }
                  @if (liga.isOwner) {
                    · liga criada por você
                  }
                </p>
                <svg class="cartao-link__seta" [appIcon]="seta" />
              </li>
            }
          </ul>
        }

        <div class="acoes-da-tela">
          <app-button
            [expanded]="painel() === 'criar'"
            controls="painel-liga"
            (pressed)="abrir('criar')"
          >
            Criar uma liga
          </app-button>
          <app-button
            variant="secondary"
            [expanded]="painel() === 'entrar'"
            controls="painel-liga"
            (pressed)="abrir('entrar')"
          >
            Entrar com um código
          </app-button>
        </div>

        @if (painel() !== 'nenhum') {
          <div #painelRef id="painel-liga" class="painel">
            @if (painel() === 'criar') {
              <app-card heading="Criar uma liga">
                <app-form-field
                  label="Nome da liga"
                  [required]="true"
                  [minLength]="nomeMin"
                  [maxLength]="nomeMax"
                  [error]="erroNome() ?? undefined"
                  hint="De 3 a 60 caracteres. É o nome que todo mundo da liga vai ver."
                  [(value)]="nome"
                />
                <p class="apoio">
                  Você entra nela automaticamente e recebe um código para convidar o grupo.
                </p>
                @if (erroEnvio(); as mensagem) {
                  <app-alert tone="danger">{{ mensagem }}</app-alert>
                }
                <div class="acoes-da-tela">
                  <app-button [loading]="enviando()" (pressed)="criar()">Criar liga</app-button>
                  <app-button variant="ghost" (pressed)="fechar()">Cancelar</app-button>
                </div>
              </app-card>
            } @else {
              <app-card heading="Entrar com um código">
                <app-form-field
                  label="Código do convite"
                  [required]="true"
                  [error]="erroCodigo() ?? undefined"
                  hint="Dez caracteres. Pode colar do jeito que veio, com hífens ou em minúsculas."
                  placeholder="ABCDE-2345K"
                  [(value)]="codigo"
                />
                @if (erroEnvio(); as mensagem) {
                  <app-alert tone="danger">{{ mensagem }}</app-alert>
                }
                <div class="acoes-da-tela">
                  <app-button [loading]="enviando()" (pressed)="entrar()">
                    Entrar na liga
                  </app-button>
                  <app-button variant="ghost" (pressed)="fechar()">Cancelar</app-button>
                </div>
              </app-card>
            }
          </div>
        }
      }
    }
  `,
  styleUrls: ['./fantasy.scss', '../../shared/ui/link-card.scss', './leagues.scss'],
})
export class FantasyLeaguesPage implements OnInit {
  protected readonly seta = ChevronRight;
  private readonly service = inject(LeagueService);
  private readonly router = inject(Router);
  private readonly title = inject(Title);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  protected readonly nomeMin = LEAGUE_NAME_MIN;
  protected readonly nomeMax = LEAGUE_NAME_MAX;

  private readonly painelRef = viewChild<ElementRef<HTMLElement>>('painelRef');

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly painel = signal<Painel>('nenhum');
  protected readonly nome = signal('');
  protected readonly codigo = signal('');
  protected readonly enviando = signal(false);
  protected readonly erroNome = signal<string | null>(null);
  protected readonly erroCodigo = signal<string | null>(null);
  protected readonly erroEnvio = signal<string | null>(null);

  protected readonly ligas = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.ligas : [];
  });

  ngOnInit(): void {
    this.title.setTitle('Ligas · Cartola Várzea');
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected participantes(total: number): string {
    return total === 1 ? '1 participante' : `${total} participantes`;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.mine(this.campeonato()).subscribe({
      next: (ligas) => this.estado.set({ tipo: 'pronto', ligas }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected abrir(painel: Painel): void {
    this.limparErros();
    this.painel.set(this.painel() === painel ? 'nenhum' : painel);
    // Quem clicou já quer digitar; o foco vai para o campo em vez de ficar no botão.
    setTimeout(() => this.painelRef()?.nativeElement.querySelector('input')?.focus());
  }

  protected fechar(): void {
    this.painel.set('nenhum');
    this.limparErros();
  }

  protected criar(): void {
    const nome = this.nome().trim();
    if (nome.length < LEAGUE_NAME_MIN || nome.length > LEAGUE_NAME_MAX) {
      this.erroNome.set(`Use de ${LEAGUE_NAME_MIN} a ${LEAGUE_NAME_MAX} caracteres no nome.`);
      return;
    }

    this.enviando.set(true);
    this.limparErros();
    this.service.create(this.campeonato(), nome).subscribe({
      next: (liga) => {
        this.enviando.set(false);
        // A liga recém-criada abre direto: é lá que está o código para compartilhar.
        void this.router.navigate(['/c', liga.competitionSlug, 'ligas', liga.id]);
      },
      error: (falha: ApiFailure) => {
        this.enviando.set(false);
        this.erroEnvio.set(
          falha.status === 404
            ? 'Você precisa estar jogando este campeonato para criar uma liga nele.'
            : falha.message,
        );
      },
    });
  }

  protected entrar(): void {
    const digitado = this.codigo();
    if (!this.pareceCodigo(digitado)) {
      this.erroCodigo.set(`O código tem ${LEAGUE_CODE_LENGTH} letras e números.`);
      return;
    }

    this.enviando.set(true);
    this.limparErros();
    this.service.join(digitado.trim()).subscribe({
      next: (liga) => {
        this.enviando.set(false);
        void this.router.navigate(['/c', liga.competitionSlug, 'ligas', liga.id]);
      },
      error: (falha: ApiFailure) => {
        this.enviando.set(false);
        this.erroEnvio.set(mensagemDeConvite(falha));
      },
    });
  }

  /**
   * O servidor tira hífens e maiúsculas antes de comparar, então a tela confere só o
   * tamanho: recusar a formatação seria recusar o código do jeito que ele chega no
   * grupo.
   */
  private pareceCodigo(digitado: string): boolean {
    return digitado.replace(/[^a-z0-9]/gi, '').length === LEAGUE_CODE_LENGTH;
  }

  private limparErros(): void {
    this.erroNome.set(null);
    this.erroCodigo.set(null);
    this.erroEnvio.set(null);
  }
}

/**
 * Código errado, vencido, de liga fechada ou de campeonato que a conta não joga recebem
 * o mesmo 404, de propósito: a resposta não confirma que a liga existe. A mensagem
 * então lista os motivos possíveis em vez de afirmar um.
 */
export function mensagemDeConvite(falha: ApiFailure): string {
  if (falha.status === 404) {
    return 'Este código não vale. Ele pode ter sido trocado, a liga pode estar fechada para novas entradas, ou ser de um campeonato que você ainda não joga.';
  }

  if (falha.code === LEAGUE_LIMIT_CODE) {
    return 'Esta liga já está cheia. Peça para alguém sair ou crie uma liga nova.';
  }

  return falha.message;
}
