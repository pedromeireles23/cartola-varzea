import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { credits } from './fantasy-format';
import { FantasyOverview } from './fantasy.service';

/**
 * Os números da montagem do elenco, iguais no Meu time e no mercado: quantas vagas
 * estão preenchidas, o saldo, o capitão e o limite por time. Cada um é uma decisão de
 * quem está escolhendo, por isso ficam lado a lado e acima da escolha.
 */
@Component({
  selector: 'app-squad-metrics',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="metricas" aria-label="Seu elenco">
      <div class="metrica">
        <p class="metrica__rotulo">Elenco</p>
        <p class="metrica__valor num">{{ escolhidos() }} de {{ vagas() }}</p>
        <div
          class="metrica__barra"
          role="progressbar"
          aria-label="Elenco montado"
          aria-valuemin="0"
          [attr.aria-valuemax]="vagas()"
          [attr.aria-valuenow]="escolhidos()"
          [attr.aria-valuetext]="escolhidos() + ' de ' + vagas() + ' vagas'"
        >
          <span [style.width.%]="progresso()"></span>
        </div>
      </div>
      <div class="metrica">
        <p class="metrica__rotulo">Saldo</p>
        <p class="metrica__valor num">{{ saldo() }}</p>
      </div>
      <div class="metrica">
        <p class="metrica__rotulo">Capitão</p>
        <p class="metrica__valor" [class.metrica__valor--pendente]="!capitao()">
          {{ capitao() ?? 'Pendente' }}
        </p>
      </div>
      <div class="metrica metrica--limite">
        <p class="metrica__rotulo">Por time</p>
        <p class="metrica__valor">
          até {{ visao().teamLimit.maxAthletes }}
          <span class="metrica__apoio">({{ visao().teamLimit.maxStarters }} titulares)</span>
        </p>
      </div>
    </section>
  `,
  styleUrl: './squad-metrics.scss',
})
export class SquadMetrics {
  readonly visao = input.required<FantasyOverview>();

  protected readonly escolhidos = computed(() => this.visao().entry?.slots.length ?? 0);
  protected readonly vagas = computed(() => this.visao().profile.squadAthletes + 1);
  protected readonly progresso = computed(() =>
    Math.round((this.escolhidos() / this.vagas()) * 100),
  );
  protected readonly saldo = computed(() => credits(this.visao().entry?.balance ?? 0));
  protected readonly capitao = computed(
    () => this.visao().entry?.slots.find((slot) => slot.isCaptain)?.name ?? null,
  );
}
