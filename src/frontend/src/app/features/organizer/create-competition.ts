import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Button, Card, Loading } from '../../shared/ui';
import { CompetitionSettingsForm } from './competition-area/competition-settings-form';
import { CompetitionCreatedState } from './competition-area/competition-layout';
import { CompetitionService, CompetitionSettings, ModalityProfile } from './competition.service';
import { MyOrganization, OrganizationService } from './organization.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | {
      readonly tipo: 'pronto';
      readonly organizacao: MyOrganization;
      readonly perfis: readonly ModalityProfile[];
    }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Criação de campeonato em rascunho (02 §9.1, `/organizar/o/:organizacao/campeonatos/novo`).
 *
 * Só o proprietário chega ao formulário; o auxiliar recebe a explicação. Depois de
 * criar, a pessoa vai direto para o resumo do campeonato novo.
 */
@Component({
  selector: 'app-create-competition',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, CompetitionSettingsForm, Loading, RouterLink],
  template: `
    <p class="rodape">
      <a [routerLink]="['/organizar/o', organizacao(), 'campeonatos']">← Campeonatos</a>
    </p>
    <h1>Novo campeonato</h1>

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card>
          <app-loading label="Preparando o formulário…" />
        </app-card>
      }

      @case ('erro') {
        <app-card>
          @if (falhaAoCarregar()!.status === 403 || falhaAoCarregar()!.status === 404) {
            <app-alert tone="warning">
              Só quem faz parte da organização cria campeonatos nela.
            </app-alert>
          } @else {
            <app-alert tone="danger">
              <p>{{ falhaAoCarregar()!.message }}</p>
              @if (falhaAoCarregar()!.traceId) {
                <p class="trace">Código de rastreio: {{ falhaAoCarregar()!.traceId }}</p>
              }
            </app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }

      @case ('pronto') {
        @if (pronto()!.organizacao.role !== 'Owner') {
          <app-card>
            <app-alert tone="info">
              Só quem é proprietário de {{ pronto()!.organizacao.name }} cria campeonatos.
            </app-alert>
          </app-card>
        } @else {
          <p class="intro">
            O campeonato nasce em rascunho em {{ pronto()!.organizacao.name }}: só a organização vê
            até a publicação, e tudo pode ser ajustado depois.
          </p>

          <div class="foco" tabindex="-1" #aviso>
            @if (falhaAoCriar()) {
              <app-alert tone="danger">{{ falhaAoCriar() }}</app-alert>
            }
          </div>

          <app-card>
            <app-competition-settings-form
              submitLabel="Criar campeonato"
              [profiles]="pronto()!.perfis"
              [saving]="criando()"
              (submitted)="criar($event)"
            />
          </app-card>
        }
      }
    }
  `,
  styleUrl: './organizer.scss',
})
export class CreateCompetitionPage implements OnInit {
  private readonly organizations = inject(OrganizationService);
  private readonly competitions = inject(CompetitionService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  /** Identificador da organização na rota. */
  readonly organizacao = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly criando = signal(false);
  protected readonly falhaAoCriar = signal<string | null>(null);

  protected readonly pronto = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual : null;
  });

  ngOnInit(): void {
    this.carregar();
  }

  protected falhaAoCarregar(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });

    forkJoin({
      organizacao: this.organizations.get(this.organizacao()),
      perfis: this.competitions.modalityProfiles(),
    }).subscribe({
      next: ({ organizacao, perfis }) => this.estado.set({ tipo: 'pronto', organizacao, perfis }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected criar(configuracao: CompetitionSettings): void {
    if (this.criando()) {
      return;
    }

    this.criando.set(true);
    this.falhaAoCriar.set(null);

    this.competitions.createDraft(this.organizacao(), configuracao).subscribe({
      next: (campeonato) => {
        const estado: CompetitionCreatedState = { criado: true };
        void this.router.navigate(['/organizar/c', campeonato.id], { state: estado });
      },
      error: (falha: ApiFailure) => {
        this.criando.set(false);
        this.falhaAoCriar.set(falha.message);
        afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
      },
    });
  }
}
