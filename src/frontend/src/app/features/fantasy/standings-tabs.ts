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
      display: flex;
      gap: var(--space-4);
      border-bottom: 1px solid var(--color-border);
    }

    .abas__item {
      display: inline-flex;
      align-items: center;
      min-height: var(--touch-target);
      padding-inline: var(--space-1);
      color: var(--color-ink-700);
      font-size: var(--font-small);
      font-weight: 500;
      text-decoration: none;
      margin-bottom: -1px;
      border-bottom: 2px solid transparent;
    }

    .abas__item:hover {
      color: var(--color-ink-950);
    }

    .abas__item[aria-current] {
      color: var(--color-ink-950);
      font-weight: 600;
      border-bottom-color: var(--color-brand-500);
    }
  `,
})
export class StandingsTabs {
  readonly campeonato = input.required<string>();
  readonly atual = input.required<StandingsTab>();

  protected readonly conta = inject(AuthService).current;
}
