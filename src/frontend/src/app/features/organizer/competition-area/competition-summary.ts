import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Alert, Card, Loading } from '../../../shared/ui';
import { MODALITY_LABELS, registrationWindowText } from '../competition.service';
import { CompetitionContext } from './competition-context';
import { PublicationProgress } from './publication-progress';
import { CompetitionReadiness, PublicationService } from './publication.service';
import {
  businessDaysText,
  formationText,
  leadTimeLabel,
  timeZoneLabel,
} from './competition-format';

/**
 * Resumo do campeonato (02 §9.1, `/organizar/c/:campeonato`).
 *
 * Mostra o que a modalidade impõe e os prazos escolhidos, em linguagem de quem
 * organiza. A situação abre com o progresso da publicação (V6): as etapas do checklist
 * e o atalho para cada pendência; o detalhe de cada impedimento fica na tela própria.
 */
@Component({
  selector: 'app-competition-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Card, DecimalPipe, Loading, PublicationProgress, RouterLink],
  template: `
    @if (campeonato(); as dados) {
      <div class="pagina">
        <h1>Resumo</h1>

        @if (acabouDeCriar) {
          <app-alert tone="success">
            Campeonato criado em rascunho. Só a sua organização vê este campeonato por enquanto.
          </app-alert>
        }

        <app-card heading="Situação">
          @if (dados.status === 'Draft') {
            <p class="apoio">
              Rascunho: só a organização vê. O checklist de publicação mostra o que ainda falta.
            </p>
          } @else {
            <p class="apoio">Publicado: o campeonato aparece para o público.</p>
          }
          @switch (prontidao().tipo) {
            @case ('carregando') {
              <app-loading label="Conferindo o checklist…" />
            }
            @case ('erro') {
              <p class="apoio">O checklist não carregou agora; ele está na tela de publicação.</p>
            }
            @case ('pronto') {
              <app-publication-progress [prontidao]="checklist()!" />
            }
          }
          <a class="acao" routerLink="publicacao">Ver checklist de publicação</a>
        </app-card>

        <app-card [heading]="'Modalidade: ' + modalidade()">
          <dl class="dados">
            <div class="dados__item">
              <dt>Titulares</dt>
              <dd>{{ dados.modalityProfile.starters }}: {{ formacao() }}</dd>
            </div>
            <div class="dados__item">
              <dt>Banco</dt>
              <dd>{{ dados.modalityProfile.benchSize }} reservas, um por posição</dd>
            </div>
            <div class="dados__item">
              <dt>Elenco de quem joga</dt>
              <dd>{{ dados.modalityProfile.squadAthletes }} atletas e 1 técnico</dd>
            </div>
            <div class="dados__item">
              <dt>Orçamento inicial</dt>
              <dd>{{ dados.modalityProfile.budget | number: '1.0-2' }} créditos</dd>
            </div>
            <div class="dados__item">
              <dt>Atletas por time real</dt>
              <dd>ao menos {{ dados.modalityProfile.minimumAthletesPerRealTeam }} inscritos</dd>
            </div>
          </dl>

          <!-- Rolável no celular: foco e nome para quem navega por teclado (axe). -->
          <div
            class="tabela"
            tabindex="0"
            role="region"
            aria-label="Limite de atletas do mesmo time real"
          >
            <table>
              <caption>
                Máximo de atletas do mesmo time real numa equipe
              </caption>
              <thead>
                <tr>
                  <th scope="col">Times ainda ativos</th>
                  <th scope="col">Entre titulares</th>
                  <th scope="col">No elenco</th>
                </tr>
              </thead>
              <tbody>
                @for (
                  limite of dados.modalityProfile.realTeamLimits;
                  track limite.activeRealTeams
                ) {
                  <tr>
                    <th scope="row">
                      {{ limite.activeRealTeams === 4 ? '4 ou mais' : limite.activeRealTeams }}
                    </th>
                    <td>{{ limite.maxStarters }}</td>
                    <td>{{ limite.maxAthletes }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </app-card>

        <app-card heading="Calendário e prazos">
          <dl class="dados">
            <div class="dados__item">
              <dt>Fuso horário</dt>
              <dd>{{ fuso() }}</dd>
            </div>
            <div class="dados__item">
              <dt>Fechamento do mercado</dt>
              <dd>{{ antecedencia() }}</dd>
            </div>
            <div class="dados__item">
              <dt>Resultado publicado em</dt>
              <dd>até {{ diasUteis(dados.resultsSlaBusinessDays) }}</dd>
            </div>
            <div class="dados__item">
              <dt>Correções aceitas por</dt>
              <dd>
                {{ diasUteis(dados.correctionWindowBusinessDays) }} após o fechamento do mercado
              </dd>
            </div>
            <div class="dados__item">
              <dt>Inscrição de atletas</dt>
              <dd>{{ inscricao() }}</dd>
            </div>
          </dl>

          @if (contexto.proprietario()) {
            <a class="acao" routerLink="configuracao">Editar configuração</a>
          }
        </app-card>
      </div>
    }
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss'],
})
export class CompetitionSummaryPage {
  protected readonly contexto = inject(CompetitionContext);
  protected readonly campeonato = this.contexto.campeonato;

  private readonly publicacao = inject(PublicationService);

  protected readonly acabouDeCriar = this.contexto.consumirCriado();

  /** O checklist é acessório: se falhar, o resumo continua de pé. */
  protected readonly prontidao = signal<
    | { readonly tipo: 'carregando' }
    | { readonly tipo: 'pronto'; readonly checklist: CompetitionReadiness }
    | { readonly tipo: 'erro' }
  >({ tipo: 'carregando' });

  protected readonly checklist = computed(() => {
    const atual = this.prontidao();
    return atual.tipo === 'pronto' ? atual.checklist : null;
  });

  constructor() {
    // A casca só abre as telas filhas com o campeonato carregado.
    const id = this.campeonato()?.id;
    if (id) {
      this.publicacao.readiness(id).subscribe({
        next: (checklist) => this.prontidao.set({ tipo: 'pronto', checklist }),
        error: () => this.prontidao.set({ tipo: 'erro' }),
      });
    }
  }

  protected readonly modalidade = computed(() => {
    const dados = this.campeonato();
    return dados ? MODALITY_LABELS[dados.modality] : '';
  });

  protected readonly formacao = computed(() => {
    const dados = this.campeonato();
    return dados ? formationText(dados.modalityProfile) : '';
  });

  protected readonly fuso = computed(() => timeZoneLabel(this.campeonato()?.timeZoneId ?? ''));

  protected readonly antecedencia = computed(() =>
    leadTimeLabel(this.campeonato()?.marketCloseLeadTimeMinutes ?? 0),
  );

  protected readonly inscricao = computed(() => {
    const atual = this.campeonato();
    return atual ? registrationWindowText(atual.registrationWindow) : '';
  });

  protected diasUteis(dias: number): string {
    return businessDaysText(dias);
  }
}
