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

import { Clock, Lock } from 'lucide';

import { Icon } from '../../shared/ui';
import { closingText, countdownText } from './fantasy-format';
import { FantasyMarketStatus } from './fantasy.service';

/**
 * Estado do mercado (02 §8, "Round status", no formato de aviso do 06 §9.4): aberto com o
 * horário absoluto do fechamento e a contagem regressiva, ou fechado.
 *
 * O horário absoluto é o texto principal; a contagem só complementa e fica fora da
 * região viva, para o leitor de tela não anunciar cada segundo. Passado o fechamento,
 * `closed` avisa a página, que recarrega o estado pelo servidor — é ele quem decide se
 * o mercado fechou. Se o servidor ainda responder "aberto" (o relógio do navegador
 * pode estar um pouco adiantado), a pergunta se repete a cada poucos segundos.
 */
/** Intervalo entre perguntas ao servidor enquanto ele insiste que o mercado está aberto. */
const REPERGUNTA_MS = 5000;

@Component({
  selector: 'app-market-clock',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    @if (market().isOpen && market().closesAtLocal) {
      <div class="relogio">
        <svg class="relogio__icone" [appIcon]="icons.clock" [size]="20" />
        <div class="relogio__texto">
          <p class="relogio__estado">Mercado aberto</p>
          <p class="relogio__prazo">{{ market().roundName }} · fecha {{ fechamento() }}</p>
          @if (contagem(); as falta) {
            <p class="relogio__contagem" aria-hidden="true">Faltam {{ falta }}</p>
          }
        </div>
      </div>
    } @else {
      <div class="relogio relogio--fechado">
        <svg class="relogio__icone" [appIcon]="icons.lock" [size]="20" />
        <div class="relogio__texto">
          <p class="relogio__estado">Mercado fechado</p>
          <p class="relogio__prazo">
            O elenco não pode ser alterado agora. Ele volta a mudar quando o organizador abrir o
            mercado da próxima rodada.
          </p>
        </div>
      </div>
    }
  `,
  styleUrl: './market-clock.scss',
})
export class MarketClock implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  readonly market = input.required<FantasyMarketStatus>();

  protected readonly icons = { clock: Clock, lock: Lock } as const;

  /** A contagem chegou a zero: hora de perguntar ao servidor. */
  readonly closed = output<void>();

  private readonly agora = signal(Date.now());

  /** Quando `closed` saiu pela última vez; zero enquanto não saiu. */
  private ultimoAviso = 0;

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
      const agora = Date.now();
      this.agora.set(agora);
      const market = this.market();
      const fechaEm = market.closesAt ? Date.parse(market.closesAt) : Number.NaN;
      if (market.isOpen && fechaEm <= agora && agora - this.ultimoAviso >= REPERGUNTA_MS) {
        this.ultimoAviso = agora;
        this.closed.emit();
      }
    }, 1000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }
}
