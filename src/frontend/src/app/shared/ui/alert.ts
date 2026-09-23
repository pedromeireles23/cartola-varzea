import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CircleAlert, CircleCheck, Info, TriangleAlert, type IconNode } from 'lucide';

import { Icon } from './icon';

export type AlertTone = 'info' | 'success' | 'warning' | 'danger';

/**
 * Aviso do design system (02 §8).
 *
 * O tom nunca depende so da cor: cada variante traz um rotulo textual, como exige
 * o principio de estado do 02 §2.
 */
@Component({
  selector: 'app-alert',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <div [class]="classes()" [attr.role]="tone() === 'danger' ? 'alert' : 'status'">
      <svg class="alert__icon" [appIcon]="icon()" />
      <strong class="alert__label">{{ label() }}</strong>
      <div class="alert__body"><ng-content /></div>
    </div>
  `,
  styleUrl: './alert.scss',
})
export class Alert {
  readonly tone = input<AlertTone>('info');

  private static readonly LABELS: Readonly<Record<AlertTone, string>> = {
    info: 'Informação',
    success: 'Tudo certo',
    warning: 'Atenção',
    danger: 'Erro',
  };

  private static readonly ICONS: Readonly<Record<AlertTone, IconNode>> = {
    info: Info,
    success: CircleCheck,
    warning: TriangleAlert,
    danger: CircleAlert,
  };

  protected readonly classes = computed(() => `alert alert--${this.tone()}`);
  protected readonly icon = computed(() => Alert.ICONS[this.tone()]);
  protected readonly label = computed(() => Alert.LABELS[this.tone()]);
}
