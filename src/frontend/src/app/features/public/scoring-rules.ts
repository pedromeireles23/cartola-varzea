import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';

import { ApiFailure } from '../../core/api/problem-details';
import { PageMetaService } from '../../core/seo/page-meta';
import { Alert, Button, Card, Loading } from '../../shared/ui';
import { formationText } from '../organizer/competition-area/competition-format';
import { MODALITY_LABELS } from '../organizer/competition.service';
import { ModalityRules, ScoringRules, ScoringRulesService } from './scoring-rules.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly regras: ScoringRules }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

const POSICOES: Readonly<Record<string, string>> = {
  Goalkeeper: 'Goleiro',
  Defender: 'Defensor',
  Midfielder: 'Meio-campista',
  Forward: 'Atacante',
};

/**
 * Pontuação e valorização (02 §9.1, `/regras`).
 *
 * Existe porque o 02 §2 exige que todo total seja explicável: quem discorda de uma
 * pontuação precisa conseguir conferir a regra sem pedir para ninguém. A página não
 * depende de campeonato — regra é catálogo versionado, e a versão aparece junto, para
 * que uma recalibração futura não faça esta página parecer a de sempre.
 */
@Component({
  selector: 'app-scoring-rules',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, Loading],
  template: `
    <h1>Como a pontuação funciona</h1>
    <p class="intro">
      Cada evento da súmula vale pontos, e quem joga acima da média da posição valoriza. As três
      modalidades usam a mesma estrutura com números calibrados para quantos gols cada uma produz.
    </p>

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card><app-loading label="Buscando as regras…" /></app-card>
      }
      @case ('erro') {
        <app-card>
          <app-alert tone="danger">{{ falha()!.message }}</app-alert>
          <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
        </app-card>
      }
      @case ('pronto') {
        <nav class="modalidades" aria-label="Modalidades">
          @for (regra of modalidades(); track regra.modality) {
            <button
              type="button"
              class="modalidades__botao"
              [class.modalidades__botao--atual]="regra.modality === escolhida()"
              [attr.aria-pressed]="regra.modality === escolhida()"
              (click)="escolher(regra.modality)"
            >
              {{ rotulo(regra) }}
            </button>
          }
        </nav>

        @if (atual(); as regra) {
          <app-card heading="O elenco">
            <dl class="dados">
              <div class="dados__item">
                <dt>Titulares</dt>
                <dd>{{ regra.profile.starters }}: {{ formacao(regra) }}</dd>
              </div>
              <div class="dados__item">
                <dt>Banco</dt>
                <dd>{{ regra.profile.benchSize }} reservas, um por posição</dd>
              </div>
              <div class="dados__item">
                <dt>Elenco</dt>
                <dd>{{ regra.profile.squadAthletes }} atletas e 1 técnico</dd>
              </div>
              <div class="dados__item">
                <dt>Orçamento</dt>
                <dd>{{ creditos(regra.profile.budget) }}</dd>
              </div>
            </dl>
          </app-card>

          <app-card heading="Quanto vale cada evento">
            <p class="apoio">
              O gol vale pela posição cadastrada do atleta, nunca pela função que ele exerceu na
              partida.
            </p>
            <ul class="eventos">
              @for (gol of regra.goalPoints; track gol.position) {
                <li class="evento">
                  <span>Gol de {{ posicao(gol.position) }}</span>
                  <strong class="evento__valor">{{ pontos(gol.points) }}</strong>
                </li>
              }
              @for (item of eventos(regra); track item.nome) {
                <li class="evento">
                  <span>{{ item.nome }}</span>
                  <strong class="evento__valor">{{ pontos(item.valor) }}</strong>
                </li>
              }
            </ul>
            <p class="apoio">
              O capitão multiplica os próprios pontos por {{ regra.captainMultiplier }} — para cima
              e para baixo. Só o primeiro amarelo da partida conta: o segundo vira vermelho.
            </p>
          </app-card>

          <app-card heading="Como o preço muda">
            <p>
              Depois da rodada, cada atleta é comparado com a média da posição dele naquela rodada.
              A diferença decide quanto o preço anda.
            </p>
            <ul class="eventos">
              @for (faixa of regra.valuation.falls; track faixa.threshold) {
                <li class="evento">
                  <span>{{ pontos(faixa.threshold) }} ou menos que a média</span>
                  <strong class="evento__valor evento__valor--queda">
                    {{ variacao(faixa.variation) }}
                  </strong>
                </li>
              }
              <li class="evento">
                <span>Entre as duas faixas</span>
                <strong class="evento__valor">preço mantido</strong>
              </li>
              @for (faixa of regra.valuation.rises; track faixa.threshold) {
                <li class="evento">
                  <span>{{ pontos(faixa.threshold) }} ou mais que a média</span>
                  <strong class="evento__valor evento__valor--alta">
                    {{ variacao(faixa.variation) }}
                  </strong>
                </li>
              }
            </ul>
            <p class="apoio">
              O preço nunca sai da faixa de {{ creditos(regra.valuation.floor) }} a
              {{ creditos(regra.valuation.ceiling) }}. Créditos são virtuais: não há pagamento,
              aposta nem prêmio.
            </p>
          </app-card>

          <p class="apoio">
            Regra versão {{ regra.version }} de {{ rotulo(regra) }}. Uma rodada já apurada guarda a
            versão que usou, então recalibrar a regra no futuro não muda resultado antigo.
          </p>
        }
      }
    }
  `,
  styleUrls: ['./public.scss', './scoring-rules.scss'],
})
export class ScoringRulesPage implements OnInit {
  private readonly service = inject(ScoringRulesService);
  private readonly meta = inject(PageMetaService);

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly escolhida = signal<string>('Fut7');

  protected readonly modalidades = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.regras.modalities : [];
  });

  protected readonly atual = computed(() =>
    this.modalidades().find((regra) => regra.modality === this.escolhida()),
  );

  ngOnInit(): void {
    this.meta.set({
      title: 'Regras de pontuação',
      description:
        'Quanto vale cada evento da súmula — gol, assistência, defesa, cartão — e como o preço do atleta sobe ou desce conforme a média da posição.',
    });
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected rotulo(regra: ModalityRules): string {
    return MODALITY_LABELS[regra.modality];
  }

  protected posicao(codigo: string): string {
    return (POSICOES[codigo] ?? codigo).toLocaleLowerCase('pt-BR');
  }

  protected formacao(regra: ModalityRules): string {
    return formationText(regra.profile);
  }

  protected escolher(modalidade: string): void {
    this.escolhida.set(modalidade);
  }

  /** Os eventos que valem o mesmo em qualquer posição, na ordem em que se lê a súmula. */
  protected eventos(regra: ModalityRules): readonly { nome: string; valor: number }[] {
    return [
      { nome: 'Assistência', valor: regra.assist },
      { nome: 'Jogo sem sofrer gol', valor: regra.cleanSheet },
      { nome: 'Gol sofrido (goleiro)', valor: regra.goalConceded },
      { nome: 'Defesa', valor: regra.goalkeeperSave },
      { nome: 'Pênalti defendido', valor: regra.penaltySave },
      { nome: 'Cartão amarelo', valor: regra.yellowCard },
      { nome: 'Cartão vermelho', valor: regra.redCard },
      { nome: 'Gol contra', valor: regra.ownGoal },
      { nome: 'Pênalti perdido', valor: regra.penaltyMiss },
    ];
  }

  protected pontos(valor: number): string {
    return `${this.numero(valor)} pts`;
  }

  /** A variação vem com sinal: o "+" some na formatação padrão e faz falta aqui. */
  protected variacao(valor: number): string {
    return `${valor > 0 ? '+' : ''}${this.numero(valor)}`;
  }

  protected creditos(valor: number): string {
    return `C$ ${this.numero(valor)}`;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.get().subscribe({
      next: (regras) => this.estado.set({ tipo: 'pronto', regras }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  private numero(valor: number): string {
    return valor.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
}
