import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../../../core/api/problem-details';
import { Alert, Button, Card, Dialog } from '../../../../shared/ui';
import { CompetitionContext } from '../competition-context';
import { PublicationProgress } from '../publication-progress';
import { NOT_READY_CODE, PublicationService, ReadinessItem } from '../publication.service';
import { SetupFooter } from './competition-setup';
import { SETUP_STEP_LABELS, SetupStep, SetupStore } from './setup-store';

/** Em que passo da montagem cada regra do checklist se resolve. */
const PASSO_DO_CODIGO: Readonly<Record<string, SetupStep>> = {
  no_stages: 'formato',
  no_stage_participants: 'formato',
  stage_without_participants: 'tabela',
  not_enough_teams: 'times',
  not_enough_athletes: 'elencos',
  thin_real_team_roster: 'elencos',
  single_price_level: 'elencos',
};

/**
 * Passo 5: o checklist de publicação de sempre, com cada pendência levando ao passo da
 * montagem que a resolve, e o botão de publicar ali mesmo. A tela Publicação continua
 * sendo o lugar de tirar do ar.
 */
@Component({
  selector: 'app-setup-publish',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, Dialog, PublicationProgress, RouterLink, SetupFooter],
  template: `
    <section class="etapa" aria-labelledby="passo-publicar">
      <h2 id="passo-publicar">5. Publicar</h2>

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
        }
      </div>

      @if (store.dados()?.readiness; as prontidao) {
        @if (prontidao.status === 'Published') {
          <p class="intro">
            O campeonato está no ar. Agora é abrir o mercado de cada rodada em Rodadas e, depois dos
            jogos, preencher as súmulas.
          </p>
          <div class="acoes">
            @if (prontidao.slug) {
              <a class="acao" [routerLink]="['/c', prontidao.slug]">Ver a página pública</a>
            }
            <a class="acao acao--principal" routerLink="../../rodadas">Ir para Rodadas</a>
          </div>
        } @else {
          <p class="intro">
            Publicar coloca o campeonato na busca e abre o jogo para os participantes. Antes disso,
            só a organização vê.
          </p>
        }

        <app-card heading="Situação">
          <app-publication-progress [prontidao]="prontidao" />
        </app-card>

        @if (itens().length > 0) {
          <app-card heading="O que falta">
            <ul class="lista">
              @for (item of itens(); track $index) {
                <li class="lista__item">
                  <span class="lista__principal">
                    {{ item.severity === 'Blocker' ? 'Impede: ' : 'Alerta: ' }}{{ item.message }}
                  </span>
                  @if (passoDe(item); as passo) {
                    <a [routerLink]="['..', passo]">Resolver em {{ rotulo(passo) }}</a>
                  }
                </li>
              }
            </ul>
          </app-card>
        }

        @if (store.jogos().length === 0 && prontidao.status !== 'Published') {
          <app-alert tone="info">
            Ainda não há jogos marcados. Dá para publicar assim, mas o jogo só começa com a tabela.
          </app-alert>
        }

        @if (prontidao.status !== 'Published') {
          <div class="acoes">
            <app-button
              escrita
              [disabled]="!prontidao.canPublish"
              [loading]="salvando()"
              (pressed)="confirmando.set(true)"
            >
              Publicar o campeonato
            </app-button>
          </div>
        }
      }

      <app-setup-footer passo="publicar" />
    </section>

    <app-dialog
      heading="Publicar o campeonato?"
      [open]="confirmando()"
      (dismissed)="confirmando.set(false)"
    >
      <p>
        {{ nome() }} passa a aparecer na busca, com endereço próprio, e quem tem conta pode entrar
        no jogo. A modalidade não muda mais depois disso.
      </p>
      <div dialogActions class="dialogo__acoes">
        <app-button variant="ghost" [disabled]="salvando()" (pressed)="confirmando.set(false)">
          Cancelar
        </app-button>
        <app-button escrita [loading]="salvando()" (pressed)="publicar()">Publicar</app-button>
      </div>
    </app-dialog>
  `,
  styleUrls: ['../../organizer.scss', '../competition-pages.scss', './setup.scss'],
})
export class SetupPublishStep {
  protected readonly store = inject(SetupStore);
  private readonly contexto = inject(CompetitionContext);
  private readonly publication = inject(PublicationService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly salvando = signal(false);
  protected readonly confirmando = signal(false);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  protected readonly nome = computed(() => this.contexto.campeonato()?.name ?? 'O campeonato');
  protected readonly itens = computed(() => this.store.dados()?.readiness.items ?? []);

  protected passoDe(item: ReadinessItem): SetupStep | null {
    return PASSO_DO_CODIGO[item.code] ?? null;
  }

  protected rotulo(passo: SetupStep): string {
    return SETUP_STEP_LABELS[passo];
  }

  protected publicar(): void {
    const prontidao = this.store.dados()?.readiness;
    if (!prontidao || this.salvando()) return;

    this.salvando.set(true);
    this.publication.setPublished(prontidao.competitionId, true, prontidao.version).subscribe({
      next: () => {
        this.salvando.set(false);
        this.confirmando.set(false);
        this.avisar('success', 'Campeonato publicado. Ele já aparece na busca.');
        // O status muda também na casca: nome, selo e o que o auxiliar pode fazer.
        this.contexto.reload();
        this.store.reload();
      },
      error: (falha: ApiFailure) => {
        this.salvando.set(false);
        this.confirmando.set(false);
        this.store.reload();
        this.avisar(
          falha.code === NOT_READY_CODE || falha.status === 409 ? 'warning' : 'danger',
          falha.message,
        );
      },
    });
  }

  private avisar(tom: 'success' | 'warning' | 'danger', texto: string): void {
    this.retorno.set({ tom, texto });
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }
}
