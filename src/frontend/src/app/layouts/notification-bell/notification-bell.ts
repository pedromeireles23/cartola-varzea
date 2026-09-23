import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Bell } from 'lucide';

import { AuthService } from '../../core/auth/auth.service';
import { NotificationService } from '../../core/notifications/notification.service';
import { Icon } from '../../shared/ui/icon';

/**
 * O sino dos avisos internos (01 §14, 02 §8).
 *
 * Abrir é ler: o painel marca tudo como lido, porque um aviso que a pessoa já viu não
 * deve continuar pedindo atenção. Cada item leva direto à pontuação da rodada. Sem
 * sessão o sino não existe, e uma falha de carregamento deixa a caixa vazia em silêncio:
 * o aviso é acessório e não pode atrapalhar a tela que a pessoa veio ver.
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
          <svg [appIcon]="bell" [size]="20" />
          @if (naoLidas() > 0) {
            <span class="sino__contador" aria-hidden="true">{{ naoLidas() }}</span>
          }
        </button>

        @if (aberto()) {
          <div id="painel-avisos" class="painel" role="region" aria-label="Avisos">
            @if (itens().length === 0) {
              <p class="painel__vazio">
                Nada por aqui ainda. Os resultados das suas rodadas aparecem neste sino.
              </p>
            } @else {
              <ul class="painel__lista">
                @for (aviso of itens(); track aviso.id) {
                  <li class="aviso" [class.aviso--lido]="aviso.read">
                    @if (aviso.roundId) {
                      <a
                        [routerLink]="['/c', aviso.competitionSlug, 'pontuacao', aviso.roundId]"
                        (click)="fechar()"
                      >
                        {{ aviso.title }}
                      </a>
                    } @else {
                      <p class="aviso__titulo">{{ aviso.title }}</p>
                    }
                    <p class="aviso__corpo">{{ aviso.body }}</p>
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
  protected readonly bell = Bell;
  protected readonly naoLidas = this.service.naoLidas;
  protected readonly itens = computed(() => this.service.inbox().items);

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

    this.service.carregar();
    this.service.marcarLidas();
  }

  protected fechar(): void {
    this.aberto.set(false);
  }
}
