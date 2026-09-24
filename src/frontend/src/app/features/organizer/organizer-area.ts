import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';

import { AuthService } from '../../core/auth/auth.service';
import { CompetitionStatus } from './competition.service';
import { OrganizationService } from './organization.service';

/** O campeonato aberto na área de organização, como a casca precisa dele. */
export interface OrganizerCompetition {
  readonly id: string;
  readonly name: string;
  readonly status: CompetitionStatus;
  readonly organizationId: string;
  readonly organizationName: string;
  /** Só o proprietário vê a configuração; a API recusa o auxiliar de qualquer forma. */
  readonly owner: boolean;
}

/**
 * A área de organização vista de fora dela (06 §4.1): quem pode entrar e, lá dentro,
 * qual campeonato está aberto.
 *
 * Quem organiza não é só quem tem o papel `Organizer`: esse papel vem da solicitação
 * aprovada, e o auxiliar convidado entra por associação à organização, sem papel
 * global. Por isso, sem o papel, a lista de organizações da conta decide. A casca lê o
 * campeonato daqui porque a navegação dele mora na barra lateral, fora da rota que o
 * carrega; quem publica é a casca do campeonato (`CompetitionLayout`).
 */
@Injectable({ providedIn: 'root' })
export class OrganizerArea {
  private readonly auth = inject(AuthService);
  private readonly organizations = inject(OrganizationService);

  private readonly memberships = signal(0);
  private readonly open = signal<OrganizerCompetition | null>(null);

  /** O campeonato aberto em `/organizar/c/:campeonato`; nulo fora dele. */
  readonly competition = this.open.asReadonly();

  readonly canOrganize = computed(() => {
    const roles = this.auth.current()?.roles ?? [];
    return roles.includes('Organizer') || roles.includes('PlatformAdmin') || this.memberships() > 0;
  });

  constructor() {
    // Só pergunta pelas organizações quando o papel não responde sozinho.
    effect(() => {
      const roles = this.auth.current()?.roles;
      if (roles && !roles.includes('Organizer') && !roles.includes('PlatformAdmin')) {
        untracked(() => this.reload());
      } else {
        this.memberships.set(0);
      }
    });
  }

  /** Relê as associações; chamado depois de aceitar um convite de auxiliar. */
  reload(): void {
    this.organizations.mine().subscribe({
      next: (items) => this.memberships.set(items.length),
      // A navegação é acessória: sem a lista, o link só não aparece.
      error: () => this.memberships.set(0),
    });
  }

  enter(competition: OrganizerCompetition): void {
    this.open.set(competition);
  }

  leave(id: string): void {
    if (this.open()?.id === id) {
      this.open.set(null);
    }
  }
}
