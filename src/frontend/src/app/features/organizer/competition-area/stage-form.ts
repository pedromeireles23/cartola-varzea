import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  linkedSignal,
  output,
  signal,
  viewChild,
} from '@angular/core';

import { Alert, Button, FormField, SelectField, SelectOption } from '../../../shared/ui';
import {
  DEFAULT_TIEBREAKERS,
  GROUP_NAME_MAX,
  MAX_GROUPS,
  STAGE_NAME_MAX,
  STAGE_NAME_MIN,
  Stage,
  StageFormat,
  StageInput,
  TIEBREAK_LABELS,
  TiebreakCriterion,
} from './stage.service';

interface GrupoEditavel {
  readonly id: string | null;
  readonly nome: string;
}

interface CriterioEditavel {
  readonly criterio: TiebreakCriterion;
  readonly usado: boolean;
}

type Campo = 'nome' | 'grupos' | 'desempate';

const LETRAS = 'ABCDEFGHIJKLMNOP';

function criteriosIniciais(stage: Stage | null): CriterioEditavel[] {
  const escolhidos = stage?.format === 'Groups' ? stage.tiebreakers : DEFAULT_TIEBREAKERS;
  const restantes = DEFAULT_TIEBREAKERS.filter((criterio) => !escolhidos.includes(criterio));
  return [
    ...escolhidos.map((criterio) => ({ criterio, usado: true })),
    ...restantes.map((criterio) => ({ criterio, usado: false })),
  ];
}

/**
 * Formulário de uma fase (02 §9.1, `/organizar/c/:campeonato/fases`).
 *
 * A ordem do desempate muda por botões, sem arrastar (02 §12). Grupos existentes
 * mantêm o identificador ao serem renomeados, porque vão receber os times na Fase 6.
 */
@Component({
  selector: 'app-stage-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, FormField, SelectField],
  template: `
    <form (submit)="enviar($event)" novalidate>
      <div class="foco" tabindex="-1" #resumo>
        @if (quantidadeDeErros() > 0) {
          <app-alert tone="danger">
            {{
              quantidadeDeErros() === 1
                ? 'Revise 1 campo antes de salvar.'
                : 'Revise ' + quantidadeDeErros() + ' campos antes de salvar.'
            }}
          </app-alert>
        }
      </div>

      <p class="apoio">
        O nome e a ordem são livres. Use o que aparece no regulamento, como Repescagem, Semifinal ou
        Final.
      </p>

      <app-form-field
        label="Nome da fase"
        placeholder="Ex.: Repescagem ou Semifinal"
        [required]="true"
        [maxLength]="nomeMax"
        [error]="erros().nome"
        [(value)]="nome"
      />

      <fieldset class="grupo">
        <legend class="grupo__titulo">Formato</legend>
        <div class="opcoes">
          <label class="opcao" [class.opcao--marcada]="formato() === 'Groups'">
            <input
              type="radio"
              name="formato"
              value="Groups"
              [checked]="formato() === 'Groups'"
              (change)="formato.set('Groups')"
            />
            <span class="opcao__texto">
              <span class="opcao__nome">Fase de grupos</span>
              <span class="opcao__detalhe">
                Times em grupos, com classificação. Um grupo só vale pontos corridos.
              </span>
            </span>
          </label>
          <label class="opcao" [class.opcao--marcada]="formato() === 'Knockout'">
            <input
              type="radio"
              name="formato"
              value="Knockout"
              [checked]="formato() === 'Knockout'"
              (change)="formato.set('Knockout')"
            />
            <span class="opcao__texto">
              <span class="opcao__nome">Mata-mata</span>
              <span class="opcao__detalhe">Confrontos eliminatórios, sem classificação.</span>
            </span>
          </label>
        </div>
      </fieldset>

      @if (formato() === 'Groups') {
        <fieldset class="grupo" [attr.aria-describedby]="erros().grupos ? 'grupos-erro' : null">
          <legend class="grupo__titulo">Grupos</legend>
          <app-select-field
            label="Quantidade de grupos"
            [options]="opcoesDeQuantidade"
            [value]="'' + grupos().length"
            (valueChange)="mudarQuantidade($event)"
          />
          @for (grupo of grupos(); track $index) {
            <app-form-field
              [label]="'Nome do grupo ' + ($index + 1)"
              [required]="true"
              [maxLength]="grupoMax"
              [value]="grupo.nome"
              (valueChange)="renomear($index, $event)"
            />
          }
          @if (erros().grupos) {
            <p class="erro" id="grupos-erro">{{ erros().grupos }}</p>
          }
        </fieldset>

        <fieldset
          class="grupo"
          [attr.aria-describedby]="erros().desempate ? 'desempate-erro' : 'desempate-dica'"
        >
          <legend class="grupo__titulo">Desempate</legend>
          <p class="apoio" id="desempate-dica">
            Vitória vale 3 pontos, empate 1 e derrota 0. Com pontos iguais, os critérios marcados
            valem nesta ordem.
          </p>
          <ol class="criterios">
            @for (item of criterios(); track item.criterio; let primeiro = $first, ultimo = $last) {
              <li class="criterio">
                <label class="criterio__marca">
                  <input
                    type="checkbox"
                    [checked]="item.usado"
                    (change)="alternar(item.criterio)"
                  />
                  {{ rotulo(item.criterio) }}
                </label>
                <span class="criterio__acoes">
                  <app-button variant="ghost" [disabled]="primeiro" (pressed)="mover($index, -1)">
                    Subir<span class="sr-only"> {{ rotulo(item.criterio) }}</span>
                  </app-button>
                  <app-button variant="ghost" [disabled]="ultimo" (pressed)="mover($index, 1)">
                    Descer<span class="sr-only"> {{ rotulo(item.criterio) }}</span>
                  </app-button>
                </span>
              </li>
            }
          </ol>
          @if (erros().desempate) {
            <p class="erro" id="desempate-erro">{{ erros().desempate }}</p>
          }
        </fieldset>
      }

      <div class="acoes">
        <app-button type="submit" [loading]="saving()">{{ submitLabel() }}</app-button>
        <app-button variant="ghost" [disabled]="saving()" (pressed)="cancelled.emit()">
          Cancelar
        </app-button>
      </div>
    </form>
  `,
  styleUrls: ['./competition-settings-form.scss', './stage-form.scss'],
})
export class StageForm {
  private readonly injector = inject(Injector);
  private readonly resumo = viewChild<ElementRef<HTMLElement>>('resumo');

  /** Fase em edição; sem ela, o formulário cria uma fase de grupos com o desempate padrão. */
  readonly initial = input<Stage | null>(null);
  readonly saving = input(false);
  readonly submitLabel = input.required<string>();

  readonly submitted = output<StageInput>();
  readonly cancelled = output<void>();

  protected readonly nomeMax = STAGE_NAME_MAX;
  protected readonly grupoMax = GROUP_NAME_MAX;
  protected readonly opcoesDeQuantidade: readonly SelectOption[] = Array.from(
    { length: MAX_GROUPS },
    (_, indice) => ({
      value: String(indice + 1),
      label: indice === 0 ? '1 grupo (pontos corridos)' : `${indice + 1} grupos`,
    }),
  );

  protected readonly nome = linkedSignal(() => this.initial()?.name ?? '');
  protected readonly formato = linkedSignal<StageFormat>(() => this.initial()?.format ?? 'Groups');
  protected readonly grupos = linkedSignal<GrupoEditavel[]>(() => {
    const existentes = this.initial()?.groups ?? [];
    return existentes.length > 0
      ? existentes.map((grupo) => ({ id: grupo.id, nome: grupo.name }))
      : [
          { id: null, nome: 'Grupo A' },
          { id: null, nome: 'Grupo B' },
        ];
  });
  protected readonly criterios = linkedSignal(() => criteriosIniciais(this.initial()));

  protected readonly erros = signal<Partial<Record<Campo, string>>>({});
  protected readonly quantidadeDeErros = computed(() => Object.keys(this.erros()).length);

  protected rotulo(criterio: TiebreakCriterion): string {
    return TIEBREAK_LABELS[criterio];
  }

  protected mudarQuantidade(valor: string): void {
    const quantidade = Number(valor);
    this.grupos.update((atuais) => {
      if (quantidade <= atuais.length) {
        return atuais.slice(0, quantidade);
      }

      const novos = [...atuais];
      for (const letra of LETRAS) {
        if (novos.length >= quantidade) {
          break;
        }

        const nome = `Grupo ${letra}`;
        if (!novos.some((grupo) => grupo.nome.toLowerCase() === nome.toLowerCase())) {
          novos.push({ id: null, nome });
        }
      }

      return novos;
    });
  }

  protected renomear(indice: number, nome: string): void {
    this.grupos.update((atuais) =>
      atuais.map((grupo, posicao) => (posicao === indice ? { ...grupo, nome } : grupo)),
    );
  }

  protected alternar(criterio: TiebreakCriterion): void {
    this.criterios.update((atuais) =>
      atuais.map((item) => (item.criterio === criterio ? { ...item, usado: !item.usado } : item)),
    );
  }

  protected mover(indice: number, direcao: -1 | 1): void {
    this.criterios.update((atuais) => {
      const destino = indice + direcao;
      if (destino < 0 || destino >= atuais.length) {
        return atuais;
      }

      const lista = [...atuais];
      [lista[indice], lista[destino]] = [lista[destino], lista[indice]];
      return lista;
    });
  }

  protected enviar(event: Event): void {
    event.preventDefault();

    const nome = this.nome().trim();
    const formato = this.formato();
    const grupos = this.grupos().map((grupo) => ({ id: grupo.id, name: grupo.nome.trim() }));
    const desempate = this.criterios()
      .filter((item) => item.usado)
      .map((item) => item.criterio);

    const erros: Partial<Record<Campo, string>> = {};
    if (nome.length < STAGE_NAME_MIN || nome.length > STAGE_NAME_MAX) {
      erros.nome = `Use de ${STAGE_NAME_MIN} a ${STAGE_NAME_MAX} caracteres.`;
    }

    if (formato === 'Groups') {
      const nomes = grupos.map((grupo) => grupo.name.toLowerCase());
      if (grupos.some((grupo) => grupo.name.length === 0 || grupo.name.length > GROUP_NAME_MAX)) {
        erros.grupos = `Dê a cada grupo um nome com até ${GROUP_NAME_MAX} caracteres.`;
      } else if (new Set(nomes).size !== nomes.length) {
        erros.grupos = 'Os grupos precisam de nomes diferentes.';
      }

      if (desempate.length === 0) {
        erros.desempate = 'Marque ao menos um critério de desempate.';
      }
    }

    this.erros.set(erros);
    if (Object.keys(erros).length > 0) {
      afterNextRender(() => this.resumo()?.nativeElement.focus(), { injector: this.injector });
      return;
    }

    this.submitted.emit(
      formato === 'Groups'
        ? { name: nome, format: formato, groups: grupos, tiebreakers: desempate }
        : { name: nome, format: formato, groups: [], tiebreakers: [] },
    );
  }
}
