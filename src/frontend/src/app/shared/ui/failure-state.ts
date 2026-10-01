import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert } from './alert';
import { Button } from './button';

/**
 * Falha ao carregar (V7): a mensagem da API, o código de rastreio quando houver e
 * "Tentar de novo". Era o mesmo bloco escrito à mão em dezenas de telas, e nem todas
 * mostravam o código — que é o que alguém do suporte pede primeiro.
 *
 * O 404 de cada tela continua fora daqui: "não encontramos" pede texto e saída próprios,
 * não uma nova tentativa.
 */
@Component({
  selector: 'app-failure-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button],
  template: `
    <div class="falha">
      <app-alert tone="danger">
        <p>{{ failure().message }}</p>
        @if (failure().traceId) {
          <p class="falha__rastreio">Código de rastreio: {{ failure().traceId }}</p>
        }
      </app-alert>
      <app-button variant="secondary" (pressed)="retry.emit()">Tentar de novo</app-button>
    </div>
  `,
  styles: `
    .falha {
      display: grid;
      gap: var(--space-3);
      justify-items: start;
    }

    p {
      margin: 0;
    }

    .falha__rastreio {
      color: var(--semantic-text-muted);
      font-size: var(--font-caption);
    }
  `,
})
export class FailureState {
  readonly failure = input.required<ApiFailure>();
  readonly retry = output();
}
