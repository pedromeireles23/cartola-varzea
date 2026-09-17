import { DatePipe, DecimalPipe } from '@angular/common';
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
import { Alert, Button, Card, Loading } from '../../shared/ui';
import { formationText, timeZoneLabel } from '../organizer/competition-area/competition-format';
import { FORMAT_LABELS } from '../organizer/competition-area/stage.service';
import { MODALITY_LABELS } from '../organizer/competition.service';
import { PublicCompetition, PublicCompetitionService } from './public-competition.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly campeonato: PublicCompetition }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Página pública do campeonato (02 §9.1, `/c/:campeonato`).
 *
 * O endereço é o slug, dado na publicação e estável a partir dali. Esta versão mostra
 * o que já existe — regras da modalidade, fases com times e o catálogo —; rodadas,
 * tabela e ranking entram com a Fase 8.
 */
@Component({
  selector: 'app-public-competition',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, DatePipe, DecimalPipe, Loading, RouterLink],
  template: `
    <p class="intro"><a routerLink="/campeonatos">← Todos os campeonatos</a></p>

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card><app-loading label="Abrindo o campeonato…" /></app-card>
      }
      @case ('erro') {
        <h1>Campeonato</h1>
        <app-card>
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Não encontramos este campeonato. Ele pode ter saído do ar ou o endereço estar errado.
            </app-alert>
            <a class="acao" routerLink="/campeonatos">Ver campeonatos publicados</a>
          } @else {
            <app-alert tone="danger">
              <p>{{ falha()!.message }}</p>
              @if (falha()!.traceId) {
                <p class="trace">Código de rastreio: {{ falha()!.traceId }}</p>
              }
            </app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }
      @case ('pronto') {
        <h1>{{ dados()!.name }}</h1>
        <p class="intro">
          {{ modalidade() }} · Temporada {{ dados()!.season }} · {{ dados()!.organizationName }}
        </p>

        <app-card heading="Como se joga">
          <dl class="dados">
            <div class="dados__item">
              <dt>Titulares</dt>
              <dd>{{ dados()!.modalityProfile.starters }}: {{ formacao() }}</dd>
            </div>
            <div class="dados__item">
              <dt>Banco</dt>
              <dd>{{ dados()!.modalityProfile.benchSize }} reservas, um por posição</dd>
            </div>
            <div class="dados__item">
              <dt>Elenco</dt>
              <dd>{{ dados()!.modalityProfile.squadAthletes }} atletas e 1 técnico</dd>
            </div>
            <div class="dados__item">
              <dt>Orçamento inicial</dt>
              <dd>{{ dados()!.modalityProfile.budget | number: '1.0-2' }} créditos</dd>
            </div>
            <div class="dados__item">
              <dt>Fuso horário</dt>
              <dd>{{ fuso() }}</dd>
            </div>
            <div class="dados__item">
              <dt>No ar desde</dt>
              <dd>{{ dados()!.publishedAt | date: 'dd/MM/yyyy' }}</dd>
            </div>
          </dl>
        </app-card>

        <app-card heading="Fases">
          @for (fase of dados()!.stages; track fase.sequence) {
            <section class="fase">
              <h3 class="fase__titulo">{{ fase.sequence }}. {{ fase.name }}</h3>
              <p class="apoio">{{ formato(fase.format) }}</p>

              @if (fase.groups.length > 0) {
                <ul class="grupos">
                  @for (grupo of fase.groups; track grupo.name) {
                    <li class="grupo">
                      <span class="grupo__nome">{{ grupo.name }}</span>
                      <span>{{ grupo.teams.join(', ') }}</span>
                    </li>
                  }
                </ul>
              } @else if (fase.teams.length > 0) {
                <p>{{ fase.teams.join(', ') }}</p>
              } @else {
                <p class="apoio">Times ainda não confirmados nesta fase.</p>
              }
            </section>
          } @empty {
            <p class="apoio">Nenhuma fase publicada.</p>
          }
        </app-card>

        <app-card heading="Times">
          <ul class="times">
            @for (time of dados()!.teams; track time.name) {
              <li class="time">
                <span>{{ time.name }}</span>
                <span class="time__elenco">{{ elenco(time.athletes) }}</span>
              </li>
            } @empty {
              <li class="apoio">Nenhum time no catálogo.</li>
            }
          </ul>
        </app-card>
      }
    }
  `,
  styleUrl: './public.scss',
})
export class PublicCompetitionPage implements OnInit {
  private readonly service = inject(PublicCompetitionService);
  private readonly title = inject(Title);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  protected readonly dados = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.campeonato : null;
  });

  protected readonly modalidade = computed(() => {
    const atual = this.dados();
    return atual ? MODALITY_LABELS[atual.modality] : '';
  });

  protected readonly formacao = computed(() => {
    const atual = this.dados();
    return atual ? formationText(atual.modalityProfile) : '';
  });

  protected readonly fuso = computed(() => timeZoneLabel(this.dados()?.timeZoneId ?? ''));

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.get(this.campeonato()).subscribe({
      next: (campeonato) => {
        this.estado.set({ tipo: 'pronto', campeonato });
        this.title.setTitle(`${campeonato.name} · Cartola Várzea`);
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected formato(format: PublicCompetition['stages'][number]['format']): string {
    return FORMAT_LABELS[format];
  }

  protected elenco(atletas: number): string {
    return `${atletas} ${atletas === 1 ? 'atleta inscrito' : 'atletas inscritos'}`;
  }
}
