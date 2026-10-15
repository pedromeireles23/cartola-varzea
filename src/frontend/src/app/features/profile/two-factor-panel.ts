import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';

import { ApiFailure } from '../../core/api/problem-details';
import {
  AuthenticatorSetup,
  TwoFactorService,
  TwoFactorStatus,
} from '../../core/auth/two-factor.service';
import { Alert, Badge, Button, FailureState, FormField, Loading } from '../../shared/ui';
import { QrCode } from './qr-code';

type Etapa =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'falha'; readonly falha: ApiFailure }
  | { readonly tipo: 'pronto'; readonly status: TwoFactorStatus }
  | { readonly tipo: 'configurando'; readonly setup: AuthenticatorSetup }
  | { readonly tipo: 'codigos'; readonly codigos: readonly string[] };

/**
 * Verificação em duas etapas na Conta (Fase 20). Aparece para o Platform admin, que só abre
 * a administração com ela ligada: o aplicativo autenticador lê o QR code, a pessoa confirma
 * com o primeiro código e recebe os códigos de recuperação, que só saem esta vez.
 */
@Component({
  selector: 'app-two-factor-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, FailureState, FormField, Loading, QrCode],
  template: `
    <section class="painel" id="duas-etapas" aria-labelledby="duas-etapas-titulo">
      <header class="painel__topo">
        <h2 id="duas-etapas-titulo" class="painel__titulo">Verificação em duas etapas</h2>
      </header>
      <div class="painel__corpo">
        @switch (etapa().tipo) {
          @case ('carregando') {
            <app-loading label="Conferindo a verificação em duas etapas…" />
          }

          @case ('falha') {
            <app-failure-state [failure]="falha()!" (retry)="carregar()" />
          }

          @case ('pronto') {
            @if (status()!.enabled) {
              <app-badge tone="success">Ligada</app-badge>
              <p class="apoio">
                Para entrar, além da senha, você digita o código do aplicativo autenticador.
                @if (status()!.recoveryCodesLeft === 1) {
                  Resta 1 código de recuperação.
                } @else {
                  Restam {{ status()!.recoveryCodesLeft }} códigos de recuperação.
                }
              </p>
            } @else {
              <p class="apoio">
                A administração da plataforma só abre com a verificação em duas etapas: além da
                senha, um código que muda a cada 30 segundos num aplicativo autenticador do celular,
                como Google Authenticator, Microsoft Authenticator ou 2FAS.
              </p>
              <app-button escrita [loading]="ocupado()" (pressed)="comecar()">
                Ligar a verificação em duas etapas
              </app-button>
            }
          }

          @case ('configurando') {
            <ol class="passos">
              <li>
                <p>No aplicativo autenticador, adicione uma conta lendo este QR code.</p>
                <app-qr-code
                  [value]="setup()!.authenticatorUri"
                  label="QR code para o aplicativo autenticador"
                />
                <p class="apoio">
                  Sem câmera? Digite a chave:
                  <code class="chave">{{ setup()!.sharedKey }}</code>
                  Ou, no celular,
                  <a [href]="setup()!.authenticatorUri">abra direto no aplicativo</a>.
                </p>
              </li>
              <li>
                <form class="confirmar" (submit)="confirmar($event)">
                  <p>Digite o código de 6 dígitos que o aplicativo mostra.</p>
                  @if (erro()) {
                    <app-alert tone="danger">{{ erro() }}</app-alert>
                  }
                  <app-form-field
                    label="Código do aplicativo"
                    autocomplete="one-time-code"
                    [required]="true"
                    [maxLength]="7"
                    [(value)]="codigo"
                  />
                  <div class="acoes">
                    <app-button type="submit" escrita [loading]="ocupado()">
                      Confirmar e ligar
                    </app-button>
                    <app-button variant="secondary" (pressed)="carregar()">Cancelar</app-button>
                  </div>
                </form>
              </li>
            </ol>
          }

          @case ('codigos') {
            <app-alert tone="success">Verificação em duas etapas ligada.</app-alert>
            <p>
              Guarde estes códigos de recuperação fora do celular, num lugar seguro. Se perder o
              aparelho, cada um deles entra uma vez no lugar do código do aplicativo. Eles não
              aparecem de novo.
            </p>
            <ol class="codigos" aria-label="Códigos de recuperação">
              @for (item of codigos(); track item) {
                <li>
                  <code>{{ item }}</code>
                </li>
              }
            </ol>
            <app-button variant="secondary" (pressed)="carregar()"
              >Já guardei os códigos</app-button
            >
          }
        }
      </div>
    </section>
  `,
  styleUrls: ['../../shared/ui/panel.scss', './two-factor-panel.scss'],
})
export class TwoFactorPanel implements OnInit {
  private readonly servico = inject(TwoFactorService);

  protected readonly etapa = signal<Etapa>({ tipo: 'carregando' });
  protected readonly codigo = signal('');
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly falha = computed(() => {
    const atual = this.etapa();
    return atual.tipo === 'falha' ? atual.falha : null;
  });

  protected readonly status = computed(() => {
    const atual = this.etapa();
    return atual.tipo === 'pronto' ? atual.status : null;
  });

  protected readonly setup = computed(() => {
    const atual = this.etapa();
    return atual.tipo === 'configurando' ? atual.setup : null;
  });

  protected readonly codigos = computed(() => {
    const atual = this.etapa();
    return atual.tipo === 'codigos' ? atual.codigos : [];
  });

  ngOnInit(): void {
    void this.carregar();
  }

  protected async carregar(): Promise<void> {
    this.etapa.set({ tipo: 'carregando' });
    this.erro.set(null);
    this.codigo.set('');

    try {
      this.etapa.set({ tipo: 'pronto', status: await this.servico.status() });
    } catch (falha) {
      this.etapa.set({ tipo: 'falha', falha: falha as ApiFailure });
    }
  }

  protected async comecar(): Promise<void> {
    this.ocupado.set(true);

    try {
      this.etapa.set({ tipo: 'configurando', setup: await this.servico.beginAuthenticator() });
    } catch (falha) {
      this.etapa.set({ tipo: 'falha', falha: falha as ApiFailure });
    } finally {
      this.ocupado.set(false);
    }
  }

  protected async confirmar(event: Event): Promise<void> {
    event.preventDefault();
    this.ocupado.set(true);
    this.erro.set(null);

    try {
      this.etapa.set({ tipo: 'codigos', codigos: await this.servico.enable(this.codigo()) });
    } catch (falha) {
      const problema = falha as ApiFailure;
      this.erro.set(
        problema.status === 400
          ? 'O código não confere. Digite o que o aplicativo mostra agora; ele muda a cada 30 segundos.'
          : problema.message,
      );
    } finally {
      this.ocupado.set(false);
    }
  }
}
