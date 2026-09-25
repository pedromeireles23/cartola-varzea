import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { AuthService } from '../../core/auth/auth.service';
import { READ_ONLY_NOTICE_ID } from '../../core/demo/read-only-mode';

/**
 * Faixa persistente do modo demonstração (02 §9.1).
 *
 * Fica nas cascas, não em cada tela, para acompanhar a pessoa por todas as áreas. O
 * texto é também a descrição dos botões de escrita indisponíveis. Quem impede a
 * escrita de verdade é o servidor, que responde `demo_read_only` a qualquer tentativa.
 */
@Component({
  selector: 'app-demo-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (demo()) {
      <aside class="demo" aria-label="Modo demonstração">
        <p class="demo__texto" [id]="noticeId">
          <strong>Modo demonstração.</strong>
          Dá para navegar por tudo; as ações que salvam ficam indisponíveis, e nada é gravado.
        </p>
      </aside>
    }
  `,
  styleUrl: './demo-banner.scss',
})
export class DemoBanner {
  protected readonly demo = inject(AuthService).isDemoViewer;
  protected readonly noticeId = READ_ONLY_NOTICE_ID;
}
