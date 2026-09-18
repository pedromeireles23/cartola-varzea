import { DecimalPipe } from '@angular/common';
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

import { Alert, Button, FormField, SelectField } from '../../../shared/ui';
import {
  BUSINESS_DAYS_MAX,
  BUSINESS_DAYS_MIN,
  COMPETITION_NAME_MAX,
  COMPETITION_NAME_MIN,
  COMPETITION_SEASON_MAX,
  CompetitionSettings,
  DEFAULT_SETTINGS,
  MARKET_CLOSE_LEAD_MAX_MINUTES,
  MODALITY_LABELS,
  Modality,
  ModalityProfile,
} from '../competition.service';
import { formationText, leadTimeOptions, timeZoneOptions } from './competition-format';

type Campo = 'nome' | 'temporada' | 'modalidade' | 'prazo' | 'janela' | 'antecedencia';

/**
 * Formulário da configuração do campeonato, usado na criação e na edição.
 *
 * Valida no navegador para responder rápido, com as mesmas regras do servidor, que
 * continua sendo quem decide. Os valores voltam ao que veio em `initial` quando ele
 * muda, como depois de carregar a versão atual num conflito de edição.
 */
@Component({
  selector: 'app-competition-settings-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, DecimalPipe, FormField, SelectField],
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

      <fieldset class="grupo">
        <legend class="grupo__titulo">Identificação</legend>
        <app-form-field
          label="Nome do campeonato"
          placeholder="Copa da Várzea"
          [required]="true"
          [maxLength]="nomeMax"
          [error]="erros().nome"
          [(value)]="nome"
        />
        <app-form-field
          label="Temporada"
          hint="Como a liga chama esta edição, por exemplo 2026 ou 1º semestre de 2026."
          [required]="true"
          [maxLength]="temporadaMax"
          [error]="erros().temporada"
          [(value)]="temporada"
        />
      </fieldset>

      <fieldset class="grupo" [attr.aria-describedby]="descricaoDaModalidade()">
        <legend class="grupo__titulo">Modalidade</legend>
        <p class="apoio">
          A modalidade define a formação, o banco e o orçamento de quem joga. Ela pode mudar
          enquanto o campeonato é rascunho.
        </p>
        @if (modalityLocked()) {
          <p class="apoio" id="modalidade-travada">
            O campeonato já foi publicado, então a modalidade não muda mais.
          </p>
        }

        <div class="opcoes">
          @for (perfil of profiles(); track perfil.modality) {
            <label class="opcao" [class.opcao--marcada]="modalidade() === perfil.modality">
              <input
                type="radio"
                name="modalidade"
                [value]="perfil.modality"
                [checked]="modalidade() === perfil.modality"
                [disabled]="modalityLocked()"
                (change)="modalidade.set(perfil.modality)"
              />
              <span class="opcao__texto">
                <span class="opcao__nome">{{ rotuloDaModalidade(perfil.modality) }}</span>
                <span class="opcao__detalhe">
                  {{ perfil.starters }} titulares: {{ formacao(perfil) }}. Banco de
                  {{ perfil.benchSize }} e {{ perfil.budget | number: '1.0-2' }} créditos.
                </span>
              </span>
            </label>
          }
        </div>
        @if (erros().modalidade) {
          <p class="erro" id="modalidade-erro">{{ erros().modalidade }}</p>
        }
      </fieldset>

      <fieldset class="grupo">
        <legend class="grupo__titulo">Calendário e prazos</legend>
        <app-select-field
          label="Fuso horário"
          hint="Os horários de partidas e do mercado aparecem neste fuso."
          [options]="opcoesDeFuso()"
          [(value)]="fuso"
        />
        <app-select-field
          label="Fechamento do mercado"
          hint="Depois disso, ninguém altera a escalação da rodada."
          [options]="opcoesDeAntecedencia()"
          [error]="erros().antecedencia"
          [(value)]="antecedencia"
        />
        <app-form-field
          label="Prazo para publicar o resultado (dias úteis)"
          type="number"
          [hint]="'Quem joga vê esse prazo enquanto espera. De ' + diasMin + ' a ' + diasMax + '.'"
          [required]="true"
          [error]="erros().prazo"
          [(value)]="prazo"
        />
        <app-form-field
          label="Janela de correção (dias úteis)"
          type="number"
          [hint]="
            'Contada a partir do fechamento do mercado; depois dela o resultado fica definitivo. De ' +
            diasMin +
            ' a ' +
            diasMax +
            '.'
          "
          [required]="true"
          [error]="erros().janela"
          [(value)]="janela"
        />
        <app-form-field
          label="Prazo de inscrição de atletas (opcional)"
          type="datetime-local"
          hint="No fuso do campeonato. Vazio usa o padrão: fechamento do mercado da última rodada da primeira fase."
          [(value)]="inscricao"
        />
      </fieldset>

      <div class="acoes">
        <app-button type="submit" [loading]="saving()">{{ submitLabel() }}</app-button>
      </div>
    </form>
  `,
  styleUrl: './competition-settings-form.scss',
})
export class CompetitionSettingsForm {
  private readonly injector = inject(Injector);
  private readonly resumo = viewChild<ElementRef<HTMLElement>>('resumo');

  /** Valores de partida; sem eles, o formulário começa vazio com os padrões do produto. */
  readonly initial = input<CompetitionSettings | null>(null);
  readonly profiles = input.required<readonly ModalityProfile[]>();
  readonly modalityLocked = input(false);
  readonly saving = input(false);
  readonly submitLabel = input.required<string>();

  readonly submitted = output<CompetitionSettings>();

  protected readonly nomeMax = COMPETITION_NAME_MAX;
  protected readonly temporadaMax = COMPETITION_SEASON_MAX;
  protected readonly diasMin = BUSINESS_DAYS_MIN;
  protected readonly diasMax = BUSINESS_DAYS_MAX;

  protected readonly nome = linkedSignal(() => this.initial()?.name ?? '');
  protected readonly temporada = linkedSignal(
    () => this.initial()?.season ?? String(new Date().getFullYear()),
  );
  protected readonly modalidade = linkedSignal<Modality | null>(
    () => this.initial()?.modality ?? null,
  );
  protected readonly fuso = linkedSignal(
    () => this.initial()?.timeZoneId ?? DEFAULT_SETTINGS.timeZoneId,
  );
  protected readonly antecedencia = linkedSignal(() =>
    String(
      this.initial()?.marketCloseLeadTimeMinutes ?? DEFAULT_SETTINGS.marketCloseLeadTimeMinutes,
    ),
  );
  protected readonly prazo = linkedSignal(() =>
    String(this.initial()?.resultsSlaBusinessDays ?? DEFAULT_SETTINGS.resultsSlaBusinessDays),
  );
  protected readonly janela = linkedSignal(() =>
    String(
      this.initial()?.correctionWindowBusinessDays ?? DEFAULT_SETTINGS.correctionWindowBusinessDays,
    ),
  );

  protected readonly inscricao = linkedSignal(
    () => this.initial()?.registrationDeadlineLocal ?? '',
  );

  protected readonly erros = signal<Partial<Record<Campo, string>>>({});
  protected readonly quantidadeDeErros = computed(() => Object.keys(this.erros()).length);

  protected readonly descricaoDaModalidade = computed(
    () =>
      [
        this.modalityLocked() ? 'modalidade-travada' : null,
        this.erros().modalidade ? 'modalidade-erro' : null,
      ]
        .filter(Boolean)
        .join(' ') || null,
  );

  protected readonly opcoesDeFuso = computed(() => timeZoneOptions(this.fuso()));
  protected readonly opcoesDeAntecedencia = computed(() =>
    leadTimeOptions(Number(this.antecedencia())),
  );

  protected rotuloDaModalidade(modalidade: Modality): string {
    return MODALITY_LABELS[modalidade];
  }

  protected formacao(perfil: ModalityProfile): string {
    return formationText(perfil);
  }

  protected enviar(event: Event): void {
    event.preventDefault();

    const nome = this.nome().trim();
    const temporada = this.temporada().trim();
    const modalidade = this.modalidade();
    const antecedencia = Number(this.antecedencia());
    const prazo = Number(this.prazo());
    const janela = Number(this.janela());

    const erros: Partial<Record<Campo, string>> = {};
    if (nome.length < COMPETITION_NAME_MIN || nome.length > COMPETITION_NAME_MAX) {
      erros.nome = `Use de ${COMPETITION_NAME_MIN} a ${COMPETITION_NAME_MAX} caracteres.`;
    }
    if (temporada.length === 0 || temporada.length > COMPETITION_SEASON_MAX) {
      erros.temporada = `Informe a temporada com até ${COMPETITION_SEASON_MAX} caracteres.`;
    }
    if (modalidade === null) {
      erros.modalidade = 'Escolha a modalidade.';
    }
    if (
      !Number.isInteger(antecedencia) ||
      antecedencia < 0 ||
      antecedencia > MARKET_CLOSE_LEAD_MAX_MINUTES
    ) {
      erros.antecedencia = 'Escolha quando o mercado fecha.';
    }
    if (!this.diasValidos(prazo)) {
      erros.prazo = `Use um número inteiro de ${BUSINESS_DAYS_MIN} a ${BUSINESS_DAYS_MAX}.`;
    }
    if (!this.diasValidos(janela)) {
      erros.janela = `Use um número inteiro de ${BUSINESS_DAYS_MIN} a ${BUSINESS_DAYS_MAX}.`;
    }

    this.erros.set(erros);
    if (Object.keys(erros).length > 0 || modalidade === null) {
      afterNextRender(() => this.resumo()?.nativeElement.focus(), { injector: this.injector });
      return;
    }

    this.submitted.emit({
      name: nome,
      season: temporada,
      modality: modalidade,
      timeZoneId: this.fuso(),
      marketCloseLeadTimeMinutes: antecedencia,
      resultsSlaBusinessDays: prazo,
      correctionWindowBusinessDays: janela,
      registrationDeadlineLocal: this.inscricao().trim() || null,
    });
  }

  private diasValidos(dias: number): boolean {
    return Number.isInteger(dias) && dias >= BUSINESS_DAYS_MIN && dias <= BUSINESS_DAYS_MAX;
  }
}
