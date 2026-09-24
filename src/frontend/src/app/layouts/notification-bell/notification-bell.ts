import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Bell, ClipboardCheck, PenLine } from 'lucide';

import { AuthService } from '../../core/auth/auth.service';
import {
  NotificationItem,
  NotificationService,
} from '../../core/notifications/notification.service';
import { Icon } from '../../shared/ui/icon';

const DIAS = ['dom.', 'seg.', 'ter.', 'qua.', 'qui.', 'sex.', 'sáb.'] as const;

/**
 * O sino dos avisos internos (01 §14, 02 §8 e 06 Parte 4).
 *
 * Abrir é ler: o painel marca tudo como lido, porque um aviso que a pessoa já viu não
 * deve continuar pedindo atenção. Mas o que chegou desde a última vez continua marcado
 * como "Novo" enquanto o painel está aberto — a resposta do servidor já vem toda lida, e
 * sem a foto tirada na abertura a pessoa não saberia o que é novidade. Cada item leva
 * direto à pontuação da rodada. Sem sessão o sino não existe, e uma falha de
 * carregamento deixa a caixa vazia em silêncio: o aviso é acessório e não pode
 * atrapalhar a tela que a pessoa veio ver.
 */
@Component({
  selector: 'app-notification-bell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, RouterLink],
  // Escape fecha o painel de onde quer que o foco esteja dentro do sino. Fica no host
  // porque o elemento que envolve o painel não é focável, e não deve ser.
  host: { '(keydown.escape)': 'fechar()' },
  template: `
    @if (conta()) {
      <div class="sino">
        <button
          type="button"
          class="sino__botao"
          [attr.aria-expanded]="aberto()"
          aria-controls="painel-avisos"
          [attr.aria-label]="rotulo()"
          (click)="alternar()"
        >
          <svg [appIcon]="icons.bell" [size]="20" />
          @if (naoLidas() > 0) {
            <span class="sino__contador" aria-hidden="true">{{ naoLidas() }}</span>
          }
        </button>

        @if (aberto()) {
          <div id="painel-avisos" class="painel" role="region" aria-labelledby="avisos-titulo">
            <p id="avisos-titulo" class="painel__titulo">Avisos</p>
            @if (itens().length === 0) {
              <p class="painel__vazio">
                Nada por aqui ainda. Os resultados das suas rodadas aparecem neste sino.
              </p>
            } @else {
              <ul class="painel__lista">
                @for (aviso of itens(); track aviso.id) {
                  <li class="aviso" [class.aviso--novo]="novo(aviso)">
                    <svg class="aviso__icone" [appIcon]="icone(aviso)" />
                    <div class="aviso__texto">
                      @if (aviso.roundId) {
                        <a
                          class="aviso__titulo"
                          [routerLink]="['/c', aviso.competitionSlug, 'pontuacao', aviso.roundId]"
                          (click)="fechar()"
                        >
                          {{ aviso.title }}
                        </a>
                      } @else {
                        <p class="aviso__titulo">{{ aviso.title }}</p>
                      }
                      <p class="aviso__corpo">{{ aviso.body }}</p>
                      <p class="aviso__meta">
                        @if (novo(aviso)) {
                          <span class="aviso__novo">Novo</span>
                        }
                        <time [attr.datetime]="aviso.createdAt">{{ quando(aviso) }}</time>
                      </p>
                    </div>
                  </li>
                }
              </ul>
            }
          </div>
        }
      </div>
    }
  `,
  styleUrl: './notification-bell.scss',
})
export class NotificationBell {
  private readonly service = inject(NotificationService);
  protected readonly conta = inject(AuthService).current;
  protected readonly aberto = signal(false);
  protected readonly icons = { bell: Bell } as const;
  protected readonly naoLidas = this.service.naoLidas;
  protected readonly itens = computed(() => this.service.inbox().items);

  /** Os avisos que estavam por ler quando o painel abriu. */
  private readonly novos = signal<ReadonlySet<string>>(new Set());

  protected readonly rotulo = computed(() =>
    this.naoLidas() === 0 ? 'Avisos' : `Avisos, ${this.naoLidas()} por ler`,
  );

  constructor() {
    if (this.conta()) this.service.carregar();
  }

  protected alternar(): void {
    const abrindo = !this.aberto();
    this.aberto.set(abrindo);
    if (!abrindo) return;

    this.novos.set(
      new Set(
        this.itens()
          .filter((aviso) => !aviso.read)
          .map((aviso) => aviso.id),
      ),
    );
    this.service.carregar();
    this.service.marcarLidas();
  }

  protected fechar(): void {
    this.aberto.set(false);
  }

  protected novo(aviso: NotificationItem): boolean {
    return !aviso.read || this.novos().has(aviso.id);
  }

  protected icone(aviso: NotificationItem) {
    return aviso.kind === 'RoundCorrected' ? PenLine : ClipboardCheck;
  }

  /** "qui., 24/09 · 16:32", no relógio de quem lê: o aviso é da conta, não do campeonato. */
  protected quando(aviso: NotificationItem): string {
    const data = new Date(aviso.createdAt);
    if (Number.isNaN(data.getTime())) return '';
    const dois = (valor: number) => String(valor).padStart(2, '0');
    return (
      `${DIAS[data.getDay()]}, ${dois(data.getDate())}/${dois(data.getMonth() + 1)}` +
      ` · ${dois(data.getHours())}:${dois(data.getMinutes())}`
    );
  }
}
