import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';

/** Onde a pessoa está dentro da classificação; `liga` é uma liga aberta, dentro de "ligas". */
export type StandingsTab = 'geral' | 'ligas' | 'liga';

/**
 * Abas da classificação (06 §4.2): o ranking geral e as ligas privadas moram na mesma
 * seção, porque as duas respondem "como estou indo". Ligas exigem conta; sem sessão, a
 * página pública do ranking fica sem abas.
 */
@Component({
  selector: 'app-standings-tabs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (conta()) {
      <nav class="abas" aria-label="Classificação">
        <a
          class="abas__item"
          [routerLink]="['/c', campeonato(), 'ranking']"
          [attr.aria-current]="atual() === 'geral' ? 'page' : null"
        >
          Geral
        </a>
        <a
          class="abas__item"
          [routerLink]="['/c', campeonato(), 'ligas']"
          [attr.aria-current]="atual() === 'ligas' ? 'page' : atual() === 'liga' ? 'true' : null"
        >
          Minhas ligas
        </a>
      </nav>
    }
  `,
  styles: `
    .abas {
      display: inline-flex;
      align-self: flex-start;
      gap: var(--space-1);
      padding: var(--space-1);
      background-color: var(--semantic-surface-inset);
      border: 1px solid var(--semantic-border-subtle);
      border-radius: var(--radius-pill);
    }

    .abas__item {
      display: inline-flex;
      align-items: center;
      min-height: var(--touch-target);
      padding-inline: var(--space-4);
      color: var(--semantic-text-muted);
      font-size: var(--font-small);
      font-weight: 700;
      text-decoration: none;
      border-radius: var(--radius-pill);
    }

    .abas__item:hover {
      color: var(--semantic-text-strong);
      background-color: var(--semantic-surface-hover);
    }

    .abas__item[aria-current] {
      color: var(--semantic-on-action);
      background-color: var(--semantic-brand);
    }
  `,
})
export class StandingsTabs {
  readonly campeonato = input.required<string>();
  readonly atual = input.required<StandingsTab>();

  protected readonly conta = inject(AuthService).current;
}
