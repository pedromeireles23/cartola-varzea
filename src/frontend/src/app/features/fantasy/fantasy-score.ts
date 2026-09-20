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
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Badge, Button, Card, Loading } from '../../shared/ui';
import {
  assetRoleLabel,
  closingText,
  credits,
  points,
  scoreItemLabel,
  signed,
  valuationGroupLabel,
} from './fantasy-format';
import { FantasyNav } from './fantasy-nav';
import { FantasyRoundScore, FantasyRoundSlot, FantasyService } from './fantasy.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly rodada: FantasyRoundScore }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

interface Grupo {
  readonly titulo: string;
  readonly vagas: readonly FantasyRoundSlot[];
}

/**
 * Pontuação detalhada de uma rodada (02 §8 "Score breakdown", `/c/:campeonato/pontuacao/:rodada`).
 *
 * Todo total abre o detalhamento: cada vaga mostra os eventos que pontuaram, quem o banco
 * cobriu, o bônus do capitão e a variação de preço explicada pela média da posição. O
 * aviso de resultado provisório fica junto dos números, não só na tela da rodada.
 */
@Component({
  selector: 'app-fantasy-score',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, FantasyNav, Loading, RouterLink],
  template: `
    <app-fantasy-nav [campeonato]="campeonato()" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Pontuação</h1>
        <app-card><app-loading label="Abrindo a pontuação…" /></app-card>
      }
      @case ('erro') {
        <h1>Pontuação</h1>
        <app-card>
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Esta rodada ainda não foi apurada, ou o endereço está errado.
            </app-alert>
            <a class="acao" [routerLink]="['/c', campeonato(), 'jogar']">Voltar ao início</a>
          } @else {
            <app-alert tone="danger">{{ falha()!.message }}</app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }
      @case ('pronto') {
        <h1>{{ pontuacao()!.roundName }}</h1>

        @if (pontuacao()!.provisional) {
          <app-alert tone="warning">
            Resultado provisório: pode mudar até {{ horario(pontuacao()!.consolidatesAtLocal) }}. A
            liga ainda pode corrigir a súmula nesse prazo.
          </app-alert>
        } @else {
          <app-alert tone="success">
            Resultado consolidado desde {{ horario(pontuacao()!.consolidatesAtLocal) }}.
          </app-alert>
        }

        @if (pontuacao()!.played) {
          <app-card>
            <p class="total">
              <span class="total__rotulo">Sua pontuação na rodada</span>
              <strong class="total__valor">{{ pontos(pontuacao()!.total) }}</strong>
            </p>
            @if (pontuacao()!.captainBonus !== 0) {
              <p class="apoio">
                Inclui {{ pontos(pontuacao()!.captainBonus) }} do capitão, que dobra a própria
                pontuação.
              </p>
            } @else if (capitaoForaDeCampo()) {
              <p class="apoio">
                O capitão não entrou em campo, então a rodada ficou sem bônus: quem entra no lugar
                dele nunca herda o multiplicador.
              </p>
            }
            <p class="apoio">
              Publicada em {{ horario(pontuacao()!.publishedAtLocal) }} · apuração
              {{ pontuacao()!.revision }}ª
            </p>
          </app-card>

          @for (grupo of grupos(); track grupo.titulo) {
            <app-card [heading]="grupo.titulo">
              <ul class="vagas">
                @for (vaga of grupo.vagas; track vaga.assetId) {
                  <li class="vaga-linha" [class.vaga-linha--fora]="!vaga.counts">
                    <div class="vaga-linha__topo">
                      <div>
                        <p class="vaga-linha__nome">
                          {{ vaga.name }}
                          @if (vaga.isCaptain) {
                            <app-badge tone="brand">Capitão</app-badge>
                          }
                        </p>
                        <p class="apoio">{{ papel(vaga) }} · {{ vaga.realTeamName }}</p>
                      </div>
                      <strong class="vaga-linha__pontos">{{ pontos(vaga.points) }}</strong>
                    </div>

                    @if (vaga.lines.length > 0) {
                      <ul class="eventos">
                        @for (linha of vaga.lines; track linha.item) {
                          <li>
                            {{ item(linha.item, linha.quantity) }}
                            <span class="eventos__pontos">{{ sinal(linha.points) }}</span>
                          </li>
                        }
                      </ul>
                    } @else if (!vaga.played) {
                      <p class="apoio">Não entrou em campo nesta rodada.</p>
                    }

                    @if (nota(vaga); as texto) {
                      <p class="vaga-linha__nota">{{ texto }}</p>
                    }
                    @if (vaga.price) {
                      <p class="vaga-linha__preco">{{ variacao(vaga) }}</p>
                    }
                  </li>
                }
              </ul>
            </app-card>
          }
        } @else {
          <app-card>
            <app-alert tone="info">
              Você não jogou esta rodada: ou entrou depois do fechamento do mercado, ou a escalação
              não estava completa quando ele fechou.
            </app-alert>
            <a class="acao" [routerLink]="['/c', campeonato(), 'escalacao']">Ver a escalação</a>
          </app-card>
        }
      }
    }
  `,
  styleUrls: ['./fantasy.scss', './fantasy-score.scss'],
})
export class FantasyScorePage implements OnInit {
  private readonly service = inject(FantasyService);
  private readonly title = inject(Title);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  /** Identificador da rodada na rota. */
  readonly rodada = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  protected readonly pontuacao = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.rodada : null;
  });

  /** Sem capitão em campo a rodada fica sem bônus, e a tela precisa dizer isso (01 §9). */
  protected readonly capitaoForaDeCampo = computed(
    () => this.pontuacao()?.slots.some((vaga) => vaga.isCaptain && !vaga.played) ?? false,
  );

  /** Titulares, banco e técnico, na ordem em que o campo os mostra. */
  protected readonly grupos = computed<readonly Grupo[]>(() => {
    const slots = this.pontuacao()?.slots ?? [];
    return [
      { titulo: 'Titulares', vagas: slots.filter((vaga) => vaga.role === 'Starter') },
      { titulo: 'Banco', vagas: slots.filter((vaga) => vaga.role === 'Bench') },
      { titulo: 'Técnico', vagas: slots.filter((vaga) => vaga.role === 'Coach') },
    ].filter((grupo) => grupo.vagas.length > 0);
  });

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected pontos(valor: number): string {
    return points(valor);
  }

  protected sinal(valor: number): string {
    return signed(valor);
  }

  protected creditos(valor: number): string {
    return credits(valor);
  }

  protected item(nome: string, quantidade: number): string {
    return scoreItemLabel(nome, quantidade);
  }

  protected papel(vaga: FantasyRoundSlot): string {
    return vaga.role === 'Bench'
      ? `Reserva de ${assetRoleLabel('Athlete', vaga.position).toLocaleLowerCase('pt-BR')}`
      : assetRoleLabel(vaga.kind, vaga.position);
  }

  protected horario(local: string): string {
    // O servidor já manda o instante no fuso do campeonato; a tela só reorganiza o texto.
    return closingText(local, this.pontuacao()?.timeZoneId ?? '');
  }

  /** O que o banco fez com a vaga, em texto e não só por cor. */
  protected nota(vaga: FantasyRoundSlot): string | null {
    if (vaga.role === 'Starter' && vaga.replacedBy) {
      return 'Não jogou: o reserva da posição entrou no lugar dele, e os pontos dele não contam.';
    }
    if (vaga.role === 'Bench') {
      return vaga.replaces
        ? 'Entrou no lugar de um titular que não jogou: os pontos dele contam.'
        : 'Ficou no banco: os pontos não contam, porque os titulares da posição jogaram.';
    }
    return vaga.counts ? null : 'Sem ninguém do time dele em campo, o técnico não pontuou.';
  }

  /**
   * A variação explicada, como pede o 01 §9: "8,00 pts · 6,11 acima da média dos
   * defensores · +1,5 · C$ 7,00 → C$ 8,50". Quem não jogou não entra na média nem muda de
   * preço, e quem ficou na média mantém o preço.
   */
  protected variacao(vaga: FantasyRoundSlot): string {
    const preco = vaga.price!;
    if (preco.difference === null) {
      return `Sem jogar, o preço não muda: ${credits(preco.newPrice)}`;
    }

    const grupo = valuationGroupLabel(vaga.kind, vaga.position);
    const distancia = Math.abs(preco.difference).toLocaleString('pt-BR', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
    const comparacao =
      preco.difference === 0
        ? `na média dos ${grupo}`
        : `${distancia} ${preco.difference > 0 ? 'acima' : 'abaixo'} da média dos ${grupo}`;
    const movimento =
      preco.variation === 0
        ? `preço mantido em ${credits(preco.newPrice)}`
        : `${signed(preco.variation)} · ${credits(preco.previousPrice)} → ${credits(preco.newPrice)}`;
    return `${points(vaga.points)} · ${comparacao} · ${movimento}`;
  }

  protected carregar(): void {
    this.service.round(this.campeonato(), this.rodada()).subscribe({
      next: (rodada) => {
        this.estado.set({ tipo: 'pronto', rodada });
        this.title.setTitle(`${rodada.roundName} · pontuação`);
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}
