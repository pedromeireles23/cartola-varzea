import { Injectable, computed, inject, signal } from '@angular/core';

import { ApiFailure } from '../../../core/api/problem-details';
import { CompetitionDetails, CompetitionService } from '../competition.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly campeonato: CompetitionDetails }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Campeonato aberto na área de organização. A casca carrega uma vez e as telas
 * internas leem daqui, para que o nome, o papel e a versão sejam os mesmos em todas.
 *
 * Provido pela casca, não pela raiz: cada campeonato aberto tem o próprio contexto.
 */
@Injectable()
export class CompetitionContext {
  private readonly service = inject(CompetitionService);
  private competitionId = '';

  readonly estado = signal<Estado>({ tipo: 'carregando' });

  readonly campeonato = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.campeonato : null;
  });

  readonly falha = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  });

  readonly proprietario = computed(() => this.campeonato()?.viewerRole === 'Owner');

  /**
   * Verdadeiro quando a pessoa chegou aqui vindo da criação. A casca registra ao nascer,
   * ainda durante a navegação; o resumo consome uma vez, então recarregar não repete.
   */
  private criadoAgora = false;

  marcarCriado(): void {
    this.criadoAgora = true;
  }

  consumirCriado(): boolean {
    const criado = this.criadoAgora;
    this.criadoAgora = false;
    return criado;
  }

  load(competitionId: string): void {
    this.competitionId = competitionId;
    this.estado.set({ tipo: 'carregando' });
    this.buscar();
  }

  /** Busca de novo sem esconder a tela, como depois de um conflito de edição. */
  reload(): void {
    this.buscar();
  }

  /** Usa a resposta de uma gravação, que já traz a versão nova. */
  replace(campeonato: CompetitionDetails): void {
    this.competitionId = campeonato.id;
    this.estado.set({ tipo: 'pronto', campeonato });
  }

  private buscar(): void {
    this.service.get(this.competitionId).subscribe({
      next: (campeonato) => this.estado.set({ tipo: 'pronto', campeonato }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}
