import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { AuthService } from '../../core/auth/auth.service';

/**
 * Faixa persistente do modo demonstração (02 §9.1).
 *
 * Fica nas cascas, não em cada tela, para acompanhar a pessoa por todas as áreas. É
 * só aviso: quem impede a escrita é o servidor, que responde com o código
 * `demo_read_only` e uma mensagem própria em qualquer tentativa.
 */
@Component({
  selector: 'app-demo-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (demo()) {
      <aside class="demo" aria-label="Modo demonstração">
        <p class="demo__texto">
          <strong>Modo demonstração.</strong>
          Dá para navegar por tudo, mas nenhuma alteração é salva.
        </p>
      </aside>
    }
  `,
  styleUrl: './demo-banner.scss',
})
export class DemoBanner {
  protected readonly demo = inject(AuthService).isDemoViewer;
}
