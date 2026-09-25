import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { ApiFailure, DEMO_READ_ONLY } from '../../core/api/problem-details';
import { AuthService } from '../../core/auth/auth.service';
import { Alert, Button, Card } from '../../shared/ui';
import { OrganizerArea } from './organizer-area';
import { InvitationService, PendingInvitation } from './invitation.service';

type Resultado =
  | { readonly tipo: 'aceitando' }
  | { readonly tipo: 'aceito' }
  | { readonly tipo: 'outra-conta' }
  | { readonly tipo: 'expirado' }
  | { readonly tipo: 'indisponivel' }
  | { readonly tipo: 'nao-encontrado' }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** Rota desta tela, usada como destino do login sem carregar o token. */
const ROTA = '/organizar/convite';

/**
 * Aceite do convite de auxiliar (02 §9.1, `/organizar/convite?token=`).
 *
 * O aceite exige confirmação explícita e a conta do e-mail convidado; a API é quem
 * confere o e-mail. A tela não é protegida pelo guard de login porque precisa ler e
 * retirar o token da URL antes de mandar a pessoa entrar.
 */
@Component({
  selector: 'app-accept-invitation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, RouterLink],
  template: `
    <h1>Convite para auxiliar</h1>

    @if (!token() && !resultado()) {
      <app-card>
        <app-alert tone="warning">
          Não encontramos o convite neste endereço. Abra de novo o link exatamente como chegou no
          e-mail.
        </app-alert>
        <a class="acao" routerLink="/organizar">Minhas organizações</a>
      </app-card>
    } @else if (!conta()) {
      <app-card heading="Entre para aceitar">
        <p class="apoio">
          Use a conta do e-mail que recebeu o convite. Se ainda não tem conta, crie uma com esse
          e-mail, confirme pelo link que vai chegar e abra de novo o link do convite.
        </p>
        <div class="acoes">
          <a class="acao" routerLink="/entrar" [queryParams]="{ destino: rota }">Entrar</a>
          <a class="acao" routerLink="/cadastro">Criar conta</a>
        </div>
      </app-card>
    } @else {
      <div class="foco" tabindex="-1" #resultadoRef>
        @switch (resultado()?.tipo) {
          @case ('aceito') {
            <app-card>
              <app-alert tone="success">
                <p>Convite aceito. Você agora auxilia a organização.</p>
              </app-alert>
              <a class="acao" routerLink="/organizar">Ver minhas organizações</a>
            </app-card>
          }
          @case ('outra-conta') {
            <app-card>
              <app-alert tone="warning">
                <p>Este convite foi enviado para outro e-mail.</p>
                <p>
                  Você entrou como <strong>{{ conta()!.email }}</strong>
                </p>
              </app-alert>
              <app-button variant="secondary" [loading]="saindo()" (pressed)="trocarDeConta()">
                Entrar com outra conta
              </app-button>
            </app-card>
          }
          @case ('expirado') {
            <app-card>
              <app-alert tone="warning">
                Este convite expirou. Peça um novo a quem organiza a liga.
              </app-alert>
            </app-card>
          }
          @case ('indisponivel') {
            <app-card>
              <app-alert tone="warning">
                Este convite não pode mais ser usado. Ele foi revogado, já foi aceito por outra
                conta ou você já faz parte da equipe.
              </app-alert>
              <a class="acao" routerLink="/organizar">Minhas organizações</a>
            </app-card>
          }
          @case ('nao-encontrado') {
            <app-card>
              <app-alert tone="warning">
                Convite não encontrado. Confira se abriu o link completo do e-mail.
              </app-alert>
            </app-card>
          }
        }
      </div>

      @if (podeAceitar()) {
        <app-card heading="Aceitar convite">
          <p class="apoio">
            Conta usada: <strong>{{ conta()!.email }}</strong>
          </p>
          @if (falhaRecuperavel(); as falha) {
            <app-alert tone="danger">{{ falha.message }}</app-alert>
          }
          <p class="apoio">
            Auxiliares lançam e revisam súmulas antes da publicação; não publicam rodadas nem
            gerenciam a equipe.
          </p>
          <app-button escrita [loading]="resultado()?.tipo === 'aceitando'" (pressed)="aceitar()">
            Aceitar convite
          </app-button>
        </app-card>
      }
    }
  `,
  styleUrl: './organizer.scss',
})
export class AcceptInvitationPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly service = inject(InvitationService);
  private readonly pendente = inject(PendingInvitation);
  private readonly injector = inject(Injector);
  private readonly area = inject(OrganizerArea);
  private readonly resultadoRef = viewChild<ElementRef<HTMLElement>>('resultadoRef');

  protected readonly rota = ROTA;
  protected readonly conta = this.auth.current;
  protected readonly token = this.pendente.current;
  protected readonly resultado = signal<Resultado | null>(null);
  protected readonly saindo = signal(false);

  protected readonly falhaRecuperavel = computed(() => {
    const atual = this.resultado();
    return atual?.tipo === 'erro' ? atual.falha : null;
  });

  /** O botão só aparece antes de uma resposta definitiva. */
  protected readonly podeAceitar = computed(() => {
    const tipo = this.resultado()?.tipo;
    return tipo === undefined || tipo === 'aceitando' || tipo === 'erro';
  });

  constructor() {
    const token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');
    if (token) {
      this.pendente.keep(token);
      // O token não fica na barra, no histórico nem num eventual destino de login.
      void this.router.navigate([], { queryParams: {}, replaceUrl: true });
    }
  }

  protected aceitar(): void {
    const token = this.token();
    if (!token || this.resultado()?.tipo === 'aceitando') {
      return;
    }

    this.resultado.set({ tipo: 'aceitando' });
    this.service.accept(token).subscribe({
      next: () => {
        // Quem acabou de virar auxiliar passa a ver "Organizar" na navegação.
        this.area.reload();
        this.concluir({ tipo: 'aceito' }, true);
      },
      error: (falha: ApiFailure) => {
        switch (falha.status) {
          case 403:
            if (falha.code === DEMO_READ_ONLY) {
              // A conta de demonstração é somente leitura: o 403 não é sobre o e-mail.
              this.resultado.set({ tipo: 'erro', falha });
              break;
            }
            // O token continua valendo para a conta certa; não é descartado.
            this.concluir({ tipo: 'outra-conta' }, false);
            break;
          case 410:
            this.concluir({ tipo: 'expirado' }, true);
            break;
          case 409:
            this.concluir({ tipo: 'indisponivel' }, true);
            break;
          case 404:
            this.concluir({ tipo: 'nao-encontrado' }, true);
            break;
          default:
            this.resultado.set({ tipo: 'erro', falha });
        }
      },
    });
  }

  protected async trocarDeConta(): Promise<void> {
    this.saindo.set(true);
    try {
      await this.auth.logout();
      await this.router.navigate(['/entrar'], { queryParams: { destino: ROTA } });
    } finally {
      this.saindo.set(false);
    }
  }

  private concluir(resultado: Resultado, descartarToken: boolean): void {
    this.resultado.set(resultado);
    if (descartarToken) {
      // A tela continua mostrando o resultado; só o token deixa de existir na memória.
      this.pendente.clear();
    }

    afterNextRender(() => this.resultadoRef()?.nativeElement.focus(), {
      injector: this.injector,
    });
  }
}
