import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Badge, Button, Card, Dialog, Loading } from '../../shared/ui';
import { credits, points } from './fantasy-format';
import { FantasyNav } from './fantasy-nav';
import { League, LeagueMember, LeagueService } from './league.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly liga: League }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * O que o diálogo está prestes a confirmar. Uma confirmação só serve as cinco ações
 * porque todas têm a mesma forma: explicar o efeito e pedir a decisão.
 */
type Confirmacao =
  | { readonly tipo: 'trocar' }
  | { readonly tipo: 'fechar' }
  | { readonly tipo: 'apagar' }
  | { readonly tipo: 'sair' }
  | { readonly tipo: 'remover'; readonly membershipId: string; readonly nome: string };

/**
 * Uma liga privada e o ranking dela (02 §9.1, `/c/:campeonato/ligas/:ligaId`).
 *
 * A rota leva o campeonato porque a API é escopada pelo slug e porque a tela precisa
 * dele para a navegação do jogo. Quem não é membro recebe o mesmo "não encontrada" de
 * uma liga que não existe, então a tela trata os dois casos com uma frase só.
 *
 * Ler e administrar são coisas separadas aqui: a classificação fica limpa, e o dono
 * entra no modo de gestão para remover alguém. Um botão de remover em cada linha
 * transformaria o ranking num muro de ações destrutivas.
 */
@Component({
  selector: 'app-fantasy-league',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Dialog, FantasyNav, Loading, RouterLink],
  template: `
    <p class="intro"><a [routerLink]="['/c', campeonato(), 'ligas']">← Minhas ligas</a></p>
    <app-fantasy-nav [campeonato]="campeonato()" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card><app-loading label="Abrindo a liga…" /></app-card>
      }
      @case ('erro') {
        <h1>Liga</h1>
        <app-card>
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Não encontramos esta liga. Ela pode ter sido apagada, ou você pode ter saído dela.
            </app-alert>
            <a class="acao" [routerLink]="['/c', campeonato(), 'ligas']">Ver minhas ligas</a>
          } @else {
            <app-alert tone="danger">{{ falha()!.message }}</app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }
      @case ('pronto') {
        <h1>{{ liga()!.name }}</h1>
        <p class="apoio">
          {{ participantes() }} · {{ liga()!.competitionName }} ·
          <a [routerLink]="['/c', liga()!.competitionSlug, 'ranking']">ver o ranking geral</a>
        </p>

        @if (aviso(); as mensagem) {
          <app-alert tone="success">{{ mensagem }}</app-alert>
        }
        @if (erroAcao(); as mensagem) {
          <app-alert tone="danger">{{ mensagem }}</app-alert>
        }

        @if (liga()!.isOwner) {
          <app-card heading="Convite">
            @if (liga()!.inviteCode; as codigo) {
              <p class="apoio">
                Quem tem este código entra na liga. Ele continua valendo, então dá para reenviar ao
                grupo semanas depois.
              </p>
              <div class="convite">
                <p class="convite__codigo" [class.convite__codigo--copiado]="copiado()">
                  <span class="sr-only">Código do convite:</span>
                  <span class="convite__valor">{{ agrupado(codigo) }}</span>
                </p>
                <app-button variant="secondary" (pressed)="copiar(codigo)">
                  {{ copiado() ? 'Copiado' : 'Copiar código' }}
                </app-button>
              </div>
              <p class="sr-only" role="status" aria-live="polite">
                {{ copiado() ? 'Código copiado.' : '' }}
              </p>
              @if (copiaManual()) {
                <p class="apoio">
                  Seu navegador não deixou copiar sozinho: selecione o código acima e copie.
                </p>
              }
              @if (liga()!.inviteExpiresAtLocal; as prazo) {
                <p class="apoio">Este código vale até {{ prazo }}.</p>
              }
              <div class="acoes-da-tela">
                <app-button variant="ghost" (pressed)="confirmar({ tipo: 'trocar' })">
                  Trocar o código
                </app-button>
                <app-button variant="ghost" (pressed)="confirmar({ tipo: 'fechar' })">
                  Fechar para novas entradas
                </app-button>
              </div>
            } @else {
              <app-alert tone="info">
                A liga está fechada para novas entradas. Quem já está dentro continua disputando.
              </app-alert>
              <app-button variant="secondary" [loading]="agindo()" (pressed)="trocar(false)">
                Gerar um código novo
              </app-button>
            }
          </app-card>
        }

        <app-card heading="Classificação">
          @if (liga()!.rounds === 0) {
            <app-alert tone="info">
              Nenhuma rodada foi apurada ainda. A classificação começa a se mexer quando o primeiro
              resultado sair.
            </app-alert>
          } @else if (liga()!.provisional) {
            <app-alert tone="warning">
              {{ liga()!.lastRoundName }} ainda é provisória: a classificação pode mudar se a liga
              corrigir a súmula.
            </app-alert>
          } @else {
            <p class="apoio">
              {{ liga()!.rounds }}
              {{ liga()!.rounds === 1 ? 'rodada apurada' : 'rodadas apuradas' }}, até
              {{ liga()!.lastRoundName }}.
            </p>
          }

          <ol class="classificacao">
            @for (membro of liga()!.members; track membro.displayName + '-' + membro.position) {
              <li class="linha" [class.linha--voce]="membro.isViewer" [style.--indice]="$index">
                <span class="linha__posicao" [attr.aria-label]="colocacao(membro.position)">
                  {{ membro.position }}º
                </span>
                <span class="linha__nome">
                  {{ membro.displayName }}
                  @if (membro.isOwner) {
                    <app-badge>Dono</app-badge>
                  }
                  @if (membro.isViewer) {
                    <app-badge tone="brand">Você</app-badge>
                  }
                  <span class="linha__detalhe">
                    <span>
                      {{ patrimonio(membro.netWorth) }}
                      @if (liga()!.rounds > 0) {
                        @if (membro.lastRoundPoints !== null) {
                          · {{ pontos(membro.lastRoundPoints) }} na última
                        } @else {
                          · não jogou a última
                        }
                      }
                      @if (membro.tied) {
                        · empatado
                      }
                    </span>
                    @if (gerenciando() && podeRemover(membro)) {
                      <button class="linha__acao" type="button" (click)="pedirRemocao(membro)">
                        Remover<span class="sr-only"> {{ membro.displayName }} da liga</span>
                      </button>
                    }
                  </span>
                </span>
                <strong class="linha__pontos">{{ pontos(membro.totalPoints) }}</strong>
              </li>
            }
          </ol>

          @if (liga()!.isOwner && liga()!.members.length > 1) {
            <app-button
              variant="ghost"
              [expanded]="gerenciando()"
              (pressed)="gerenciando.set(!gerenciando())"
            >
              {{ gerenciando() ? 'Concluir' : 'Gerenciar participantes' }}
            </app-button>
          }
        </app-card>

        @if (liga()!.isOwner) {
          <app-card heading="Apagar a liga">
            <p class="apoio">
              Quem criou a liga não sai dela: apaga. Ninguém perde pontos e a classificação do
              campeonato continua igual — só a liga deixa de existir para todo mundo.
            </p>
            <app-button variant="danger" (pressed)="confirmar({ tipo: 'apagar' })">
              Apagar esta liga
            </app-button>
          </app-card>
        } @else {
          <app-card heading="Sair da liga">
            <p class="apoio">
              Você sai da classificação desta liga e continua jogando o campeonato normalmente. Para
              voltar, vai precisar do código de convite de novo.
            </p>
            <app-button variant="secondary" (pressed)="confirmar({ tipo: 'sair' })">
              Sair desta liga
            </app-button>
          </app-card>
        }

        <app-dialog
          [open]="confirmacao() !== null"
          [heading]="tituloDaConfirmacao()"
          (dismissed)="confirmacao.set(null)"
        >
          @switch (confirmacao()?.tipo) {
            @case ('trocar') {
              <p>
                O código de agora para de funcionar na hora. Quem ainda não entrou vai precisar do
                código novo; quem já está dentro continua na liga.
              </p>
            }
            @case ('fechar') {
              <p>
                A liga para de aceitar entradas e o código deixa de valer. Dá para gerar um código
                novo depois, quando você quiser reabrir.
              </p>
            }
            @case ('apagar') {
              <p>
                A liga e a lista de participantes somem para todo mundo, e isso não pode ser
                desfeito. A pontuação de cada pessoa no campeonato continua igual.
              </p>
            }
            @case ('sair') {
              <p>
                Você sai da classificação desta liga. Para voltar, vai precisar do código de convite
                de novo.
              </p>
            }
            @case ('remover') {
              <p>
                {{ confirmacaoRemover()?.nome }} sai da classificação desta liga. Para voltar, essa
                pessoa vai precisar do código de convite.
              </p>
            }
          }
          <div dialogActions>
            <app-button variant="ghost" (pressed)="confirmacao.set(null)">Cancelar</app-button>
            <app-button
              [variant]="varianteDaConfirmacao()"
              [loading]="agindo()"
              (pressed)="executar()"
            >
              {{ acaoDaConfirmacao() }}
            </app-button>
          </div>
        </app-dialog>
      }
    }
  `,
  styleUrls: ['./fantasy.scss', '../../shared/ui/leaderboard.scss', './leagues.scss'],
})
export class FantasyLeaguePage implements OnInit {
  private readonly service = inject(LeagueService);
  private readonly router = inject(Router);
  private readonly title = inject(Title);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  /**
   * Identificador da liga na rota. O parâmetro se chama `ligaId`, e não `liga`, porque
   * `liga()` aqui já é a liga carregada; o nome do parâmetro não aparece no endereço.
   */
  readonly ligaId = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly confirmacao = signal<Confirmacao | null>(null);
  protected readonly gerenciando = signal(false);
  protected readonly agindo = signal(false);
  protected readonly copiado = signal(false);
  protected readonly copiaManual = signal(false);
  protected readonly aviso = signal<string | null>(null);
  protected readonly erroAcao = signal<string | null>(null);

  protected readonly liga = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.liga : null;
  });

  protected readonly confirmacaoRemover = computed(() => {
    const atual = this.confirmacao();
    return atual?.tipo === 'remover' ? atual : null;
  });

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected participantes(): string {
    const total = this.liga()?.members.length ?? 0;
    return total === 1 ? '1 participante' : `${total} participantes`;
  }

  /** "3º" sai como "terceiro lugar" no leitor de tela, que não lê o º sozinho. */
  protected colocacao(posicao: number): string {
    return `${posicao}º lugar`;
  }

  protected pontos(valor: number): string {
    return points(valor);
  }

  protected patrimonio(valor: number): string {
    return credits(valor);
  }

  /** Dez caracteres seguidos são difíceis de ditar; em dois blocos de cinco, não. */
  protected agrupado(codigo: string): string {
    return `${codigo.slice(0, 5)}-${codigo.slice(5)}`;
  }

  /** O servidor só manda `membershipId` de quem esta conta pode tirar da liga. */
  protected podeRemover(membro: LeagueMember): boolean {
    return !membro.isViewer && membro.membershipId !== null;
  }

  protected pedirRemocao(membro: LeagueMember): void {
    if (membro.membershipId === null) {
      return;
    }

    this.confirmar({
      tipo: 'remover',
      membershipId: membro.membershipId,
      nome: membro.displayName,
    });
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.get(this.campeonato(), this.ligaId()).subscribe({
      next: (liga) => {
        this.estado.set({ tipo: 'pronto', liga });
        this.title.setTitle(`${liga.name} · ${liga.competitionName}`);
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected async copiar(codigo: string): Promise<void> {
    const area = navigator.clipboard;
    if (!area) {
      this.copiaManual.set(true);
      return;
    }

    try {
      await area.writeText(codigo);
      this.copiado.set(true);
      this.copiaManual.set(false);
      setTimeout(() => this.copiado.set(false), 2400);
    } catch {
      this.copiaManual.set(true);
    }
  }

  protected confirmar(acao: Confirmacao): void {
    this.erroAcao.set(null);
    this.aviso.set(null);
    this.confirmacao.set(acao);
  }

  protected tituloDaConfirmacao(): string {
    switch (this.confirmacao()?.tipo) {
      case 'trocar':
        return 'Trocar o código do convite?';
      case 'fechar':
        return 'Fechar a liga para novas entradas?';
      case 'apagar':
        return `Apagar a liga ${this.liga()?.name}?`;
      case 'sair':
        return `Sair da liga ${this.liga()?.name}?`;
      case 'remover':
        return `Remover ${this.confirmacaoRemover()?.nome} da liga?`;
      default:
        return '';
    }
  }

  protected acaoDaConfirmacao(): string {
    switch (this.confirmacao()?.tipo) {
      case 'trocar':
        return 'Trocar o código';
      case 'fechar':
        return 'Fechar a liga';
      case 'apagar':
        return 'Apagar a liga';
      case 'sair':
        return 'Sair da liga';
      case 'remover':
        return 'Remover';
      default:
        return '';
    }
  }

  protected varianteDaConfirmacao(): 'primary' | 'danger' {
    const tipo = this.confirmacao()?.tipo;
    return tipo === 'apagar' || tipo === 'remover' || tipo === 'sair' ? 'danger' : 'primary';
  }

  protected executar(): void {
    const acao = this.confirmacao();
    const liga = this.liga();
    if (!acao || !liga) {
      return;
    }

    switch (acao.tipo) {
      case 'trocar':
        this.trocar(false);
        return;
      case 'fechar':
        this.trocar(true);
        return;
      case 'apagar':
        this.encerrar(this.service.remove(this.campeonato(), liga.id, liga.version));
        return;
      case 'sair':
        this.encerrar(this.sairDaLiga(liga));
        return;
      case 'remover':
        this.aplicar(
          this.service.removeMember(this.campeonato(), liga.id, acao.membershipId),
          `${acao.nome} saiu da liga.`,
        );
        return;
    }
  }

  protected trocar(fechar: boolean): void {
    const liga = this.liga();
    if (!liga) {
      return;
    }

    this.aplicar(
      this.service.rotateInvite(this.campeonato(), liga.id, liga.version, fechar),
      fechar ? 'A liga está fechada para novas entradas.' : 'Código novo gerado.',
    );
  }

  /** O próprio participante saindo: a linha dele é a dele mesmo, com `membershipId`. */
  private sairDaLiga(liga: League): Observable<void> {
    const minha = liga.members.find((membro) => membro.isViewer)?.membershipId;
    return this.service.removeMember(this.campeonato(), liga.id, minha ?? '');
  }

  /** Ação que mantém a pessoa na tela: recarrega a liga e confirma o que mudou. */
  private aplicar(requisicao: Observable<unknown>, mensagem: string): void {
    this.agindo.set(true);
    requisicao.subscribe({
      next: () => {
        this.agindo.set(false);
        this.confirmacao.set(null);
        this.aviso.set(mensagem);
        this.carregar();
      },
      error: (falha: ApiFailure) => this.falhou(falha),
    });
  }

  /** Ação que tira a pessoa da liga: não há para onde voltar a não ser a lista. */
  private encerrar(requisicao: Observable<unknown>): void {
    this.agindo.set(true);
    requisicao.subscribe({
      next: () => {
        this.agindo.set(false);
        this.confirmacao.set(null);
        void this.router.navigate(['/c', this.campeonato(), 'ligas']);
      },
      error: (falha: ApiFailure) => this.falhou(falha),
    });
  }

  private falhou(falha: ApiFailure): void {
    this.agindo.set(false);
    this.confirmacao.set(null);
    this.erroAcao.set(falha.message);
  }
}
