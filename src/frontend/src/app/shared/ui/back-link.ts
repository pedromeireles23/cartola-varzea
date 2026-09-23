import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ArrowLeft } from 'lucide';

import { Icon } from './icon';

/**
 * Link para a tela de cima, com a seta da biblioteca de ícones (06 §8) em vez de um
 * caractere "←". O texto diz para onde vai, e não "voltar": quem chega por um link
 * compartilhado não veio de lugar nenhum.
 */
@Component({
  selector: 'app-back-link',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, RouterLink],
  template: `
    <a class="voltar" [routerLink]="link()"> <svg [appIcon]="seta" [size]="16" />{{ label() }} </a>
  `,
  styles: `
    :host {
      display: block;
    }

    .voltar {
      display: inline-flex;
      gap: var(--space-1);
      align-items: center;
      min-height: var(--touch-target);
      color: var(--color-ink-700);
      font-size: var(--font-small);
      font-weight: 500;
      text-decoration: none;
    }

    .voltar:hover {
      color: var(--color-ink-950);
      text-decoration: underline;
    }
  `,
})
export class BackLink {
  readonly link = input.required<string | readonly unknown[]>();
  readonly label = input.required<string>();

  protected readonly seta = ArrowLeft;
}
