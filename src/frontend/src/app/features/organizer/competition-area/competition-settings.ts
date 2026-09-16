import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';

import { ApiFailure, COMPETITION_MODALITY_LOCKED } from '../../../core/api/problem-details';
import { Alert, Button, Card, Loading } from '../../../shared/ui';
import { CompetitionService, CompetitionSettings, ModalityProfile } from '../competition.service';
import { CompetitionContext } from './competition-context';
import { CompetitionSettingsForm } from './competition-settings-form';

type Retorno =
  | { readonly tipo: 'salvo' }
  | { readonly tipo: 'conflito' }
  | { readonly tipo: 'falha'; readonly texto: string };

/**
 * Configuração do campeonato (02 §9.1, `/organizar/c/:campeonato/configuracao`).
 *
 * A edição salva sobre a versão lida. Se alguém salvou antes, a tela não sobrescreve:
 * explica o conflito e oferece carregar a versão atual.
 */
@Component({
  selector: 'app-competition-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, CompetitionSettingsForm, Loading],
  template: `
    <div class="pagina">
      <h1>Configuração</h1>

      <div class="foco" tabindex="-1" #aviso>
        @switch (retorno()?.tipo) {
          @case ('salvo') {
            <app-alert tone="success">Configuração salva.</app-alert>
          }
          @case ('conflito') {
            <app-alert tone="warning">
              <p>
                Alguém salvou esta configuração enquanto você editava. Carregue a versão atual e
                refaça suas mudanças.
              </p>
            </app-alert>
            <app-button variant="secondary" (pressed)="carregarVersaoAtual()">
              Carregar versão atual
            </app-button>
          }
          @case ('falha') {
            <app-alert tone="danger">{{ textoDaFalha() }}</app-alert>
          }
        }
      </div>

      @if (!contexto.proprietario()) {
        <app-card>
          <app-alert tone="info">
            Só quem é proprietário da organização altera a configuração.
          </app-alert>
        </app-card>
      } @else {
        @switch (perfis().tipo) {
          @case ('carregando') {
            <app-card>
              <app-loading label="Buscando as modalidades…" />
            </app-card>
          }
          @case ('erro') {
            <app-card>
              <app-alert tone="danger">Não foi possível carregar as modalidades.</app-alert>
              <app-button variant="secondary" (pressed)="carregarPerfis()">
                Tentar de novo
              </app-button>
            </app-card>
          }
          @case ('pronto') {
            <app-card>
              <app-competition-settings-form
                submitLabel="Salvar configuração"
                [initial]="contexto.campeonato()"
                [profiles]="listaDePerfis()"
                [modalityLocked]="!contexto.campeonato()!.canChangeModality"
                [saving]="salvando()"
                (submitted)="salvar($event)"
              />
            </app-card>
          }
        }
      }
    </div>
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss'],
})
export class CompetitionSettingsPage {
  private readonly service = inject(CompetitionService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly contexto = inject(CompetitionContext);
  protected readonly salvando = signal(false);
  protected readonly retorno = signal<Retorno | null>(null);
  protected readonly perfis = signal<
    | { readonly tipo: 'carregando' }
    | { readonly tipo: 'pronto'; readonly lista: readonly ModalityProfile[] }
    | { readonly tipo: 'erro' }
  >({ tipo: 'carregando' });

  constructor() {
    // A casca só mostra esta tela com o campeonato carregado, então o papel já é conhecido.
    if (this.contexto.proprietario()) {
      this.carregarPerfis();
    }
  }

  protected listaDePerfis(): readonly ModalityProfile[] {
    const atual = this.perfis();
    return atual.tipo === 'pronto' ? atual.lista : [];
  }

  protected textoDaFalha(): string {
    const atual = this.retorno();
    return atual?.tipo === 'falha' ? atual.texto : '';
  }

  protected carregarPerfis(): void {
    this.perfis.set({ tipo: 'carregando' });
    this.service.modalityProfiles().subscribe({
      next: (lista) => this.perfis.set({ tipo: 'pronto', lista }),
      error: () => this.perfis.set({ tipo: 'erro' }),
    });
  }

  protected salvar(configuracao: CompetitionSettings): void {
    const atual = this.contexto.campeonato();
    if (!atual || this.salvando()) {
      return;
    }

    this.salvando.set(true);
    this.retorno.set(null);

    this.service.updateSettings(atual.id, configuracao, atual.version).subscribe({
      next: (campeonato) => {
        this.salvando.set(false);
        this.contexto.replace(campeonato);
        this.avisar({ tipo: 'salvo' });
      },
      error: (falha: ApiFailure) => {
        this.salvando.set(false);
        this.avisar(
          falha.status === 409 && falha.code !== COMPETITION_MODALITY_LOCKED
            ? { tipo: 'conflito' }
            : { tipo: 'falha', texto: falha.message },
        );
      },
    });
  }

  protected carregarVersaoAtual(): void {
    this.retorno.set(null);
    this.contexto.reload();
  }

  private avisar(retorno: Retorno): void {
    this.retorno.set(retorno);
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }
}
