import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';

import { Badge } from '../../shared/ui';
import { closingText, countdownText } from './fantasy-format';
import { FantasyMarketStatus } from './fantasy.service';

/**
 * Estado do mercado (02 §8, "Round status"): aberto com o horário absoluto do
 * fechamento e a contagem regressiva, ou fechado.
 *
 * O horário absoluto é o texto principal; a contagem só complementa e fica fora da
 * região viva, para o leitor de tela não anunciar cada segundo. Quando ela chega a
 * zero, `closed` avisa a página, que recarrega o estado pelo servidor — é ele quem
 * decide se o mercado fechou.
 */
@Component({
  selector: 'app-market-clock',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Badge],
  template: `
    @if (market().isOpen && market().closesAtLocal) {
      <div class="relogio">
        <app-badge tone="success">Mercado aberto</app-badge>
        <p class="relogio__texto">{{ market().roundName }} · fecha {{ fechamento() }}</p>
        @if (contagem(); as falta) {
          <p class="relogio__contagem" aria-hidden="true">Faltam {{ falta }}</p>
        }
      </div>
    } @else {
      <div class="relogio">
        <app-badge tone="neutral">Mercado fechado</app-badge>
        <p class="relogio__texto">
          O elenco não pode ser alterado agora. Ele volta a mudar quando o organizador abrir o
          mercado da próxima rodada.
        </p>
      </div>
    }
  `,
  styleUrl: './market-clock.scss',
})
export class MarketClock implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  readonly market = input.required<FantasyMarketStatus>();

  /** A contagem chegou a zero: hora de perguntar ao servidor. */
  readonly closed = output<void>();

  private readonly agora = signal(Date.now());
  private avisado = false;

  protected readonly fechamento = computed(() => {
    const market = this.market();
    return market.closesAtLocal ? closingText(market.closesAtLocal, market.timeZoneId) : '';
  });

  protected readonly contagem = computed(() => {
    const closesAt = this.market().closesAt;
    return closesAt ? countdownText(closesAt, this.agora()) : null;
  });

  ngOnInit(): void {
    const timer = setInterval(() => {
      this.agora.set(Date.now());
      if (this.market().isOpen && this.contagem() === null && !this.avisado) {
        this.avisado = true;
        this.closed.emit();
      }
    }, 1000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }
}
