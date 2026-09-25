import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';

import { ApiFailure } from '../../core/api/problem-details';
import {
  Alert,
  BackLink,
  Badge,
  BadgeTone,
  Button,
  Card,
  Dialog,
  FormField,
  Loading,
} from '../../shared/ui';
import {
  InvitationStatus,
  OrganizationAssistant,
  OrganizationInvitation,
  OrganizationService,
  OrganizationTeam,
} from './organization.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly equipe: OrganizationTeam }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

const STATUS: Readonly<
  Record<InvitationStatus, { readonly rotulo: string; readonly tom: BadgeTone }>
> = {
  Pending: { rotulo: 'Pendente', tom: 'warning' },
  Accepted: { rotulo: 'Aceito', tom: 'success' },
  Revoked: { rotulo: 'Revogado', tom: 'neutral' },
  Expired: { rotulo: 'Expirado', tom: 'neutral' },
};

const EMAIL_MAX = 254;
const EMAIL_VALIDO = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Equipe auxiliar de uma organização (02 §9.1, `/organizar/o/:organizacao/equipe`).
 *
 * Só o proprietário chega aos dados: a API decide pela associação com a organização
 * da rota, e trocar o ID na URL resulta no estado sem permissão, não em outra equipe.
 */
@Component({
  selector: 'app-organization-team',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, BackLink, Badge, Button, Card, DatePipe, Dialog, FormField, Loading],
  template: `
    <app-back-link link="/organizar" label="Minhas organizações" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Equipe</h1>
        <app-card>
          <app-loading label="Buscando a equipe…" />
        </app-card>
      }

      @case ('erro') {
        <h1>Equipe</h1>
        <app-card>
          @if (falha()!.status === 403 || falha()!.status === 404) {
            <app-alert tone="warning">
              Só quem é proprietário da organização gerencia a equipe.
            </app-alert>
          } @else {
            <app-alert tone="danger">
              <p>{{ falha()!.message }}</p>
              @if (falha()!.traceId) {
                <p class="trace">Código de rastreio: {{ falha()!.traceId }}</p>
              }
            </app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }

      @case ('pronto') {
        <h1>Equipe de {{ equipe()!.organizationName }}</h1>
        <p class="intro">
          Auxiliares lançam e revisam súmulas antes da publicação. Não publicam rodadas nem
          gerenciam a equipe.
        </p>

        <app-card heading="Convidar auxiliar">
          <form (submit)="convidar($event)" novalidate>
            <div class="foco" tabindex="-1" #aviso>
              @if (retorno(); as resultado) {
                <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
              }
            </div>

            <app-form-field
              label="E-mail de quem vai auxiliar"
              type="email"
              autocomplete="off"
              hint="A pessoa precisa entrar com uma conta deste e-mail para aceitar."
              [required]="true"
              [maxLength]="emailMax"
              [error]="erroDoEmail() ?? undefined"
              [(value)]="email"
            />

            <app-button escrita type="submit" [loading]="convidando()">Enviar convite</app-button>
          </form>
        </app-card>

        <app-card heading="Auxiliares">
          @if (equipe()!.assistants.length > 0) {
            <ul class="lista">
              @for (pessoa of equipe()!.assistants; track pessoa.userId) {
                <li class="lista__item">
                  <span class="lista__principal">{{ pessoa.displayName }}</span>
                  <app-button escrita variant="ghost" (pressed)="pedirRemocao(pessoa)"
                    >Remover</app-button
                  >
                  <span class="lista__detalhe">
                    {{ pessoa.email }} · desde {{ pessoa.joinedAt | date: formatoData }}
                  </span>
                </li>
              }
            </ul>
          } @else {
            <p class="apoio">Ninguém auxilia esta organização ainda.</p>
          }
        </app-card>

        <app-card heading="Convites">
          @if (equipe()!.invitations.length > 0) {
            <ul class="lista">
              @for (convite of equipe()!.invitations; track convite.id) {
                <li class="lista__item">
                  <span class="lista__principal">{{ convite.invitedEmail }}</span>
                  <app-badge [tone]="status(convite).tom">{{ status(convite).rotulo }}</app-badge>
                  @if (convite.status === 'Pending') {
                    <app-button escrita variant="ghost" (pressed)="pedirRevogacao(convite)">
                      Revogar
                    </app-button>
                  }
                  <span class="lista__detalhe">
                    Enviado em {{ convite.createdAt | date: formatoData }}
                    @if (convite.status === 'Pending') {
                      · vale até {{ convite.expiresAt | date: formatoData }}
                    }
                  </span>
                </li>
              }
            </ul>
          } @else {
            <p class="apoio">Nenhum convite enviado.</p>
          }
        </app-card>
      }
    }

    <app-dialog
      [heading]="'Revogar convite de ' + (revogando()?.invitedEmail ?? '') + '?'"
      [open]="revogando() !== null"
      (dismissed)="cancelarRevogacao()"
    >
      <p>O link enviado deixa de funcionar. Se precisar, você pode convidar de novo depois.</p>
      @if (falhaAoRevogar()) {
        <app-alert tone="danger">{{ falhaAoRevogar() }}</app-alert>
      }
      <div dialogActions class="dialogo__acoes">
        <app-button variant="ghost" [disabled]="confirmando()" (pressed)="cancelarRevogacao()">
          Cancelar
        </app-button>
        <app-button escrita variant="danger" [loading]="confirmando()" (pressed)="revogar()">
          Revogar convite
        </app-button>
      </div>
    </app-dialog>

    <app-dialog
      [heading]="'Remover ' + (removendo()?.displayName ?? '') + ' da equipe?'"
      [open]="removendo() !== null"
      (dismissed)="cancelarRemocao()"
    >
      <p>
        O acesso à organização acaba na hora, mesmo com a sessão aberta. Para voltar, a pessoa
        precisa de um convite novo.
      </p>
      @if (falhaAoRemover()) {
        <app-alert tone="danger">{{ falhaAoRemover() }}</app-alert>
      }
      <div dialogActions class="dialogo__acoes">
        <app-button variant="ghost" [disabled]="confirmando()" (pressed)="cancelarRemocao()">
          Cancelar
        </app-button>
        <app-button escrita variant="danger" [loading]="confirmando()" (pressed)="remover()">
          Remover da equipe
        </app-button>
      </div>
    </app-dialog>
  `,
  styleUrl: './organizer.scss',
})
export class OrganizationTeamPage implements OnInit {
  private readonly service = inject(OrganizationService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  /** Identificador da organização na rota. */
  readonly organizacao = input.required<string>();

  protected readonly emailMax = EMAIL_MAX;
  protected readonly formatoData = "d MMM y 'às' HH'h'mm";

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly email = signal('');
  protected readonly erroDoEmail = signal<string | null>(null);
  protected readonly convidando = signal(false);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'danger';
    readonly texto: string;
  } | null>(null);

  protected readonly revogando = signal<OrganizationInvitation | null>(null);
  protected readonly confirmando = signal(false);
  protected readonly falhaAoRevogar = signal<string | null>(null);

  protected readonly removendo = signal<OrganizationAssistant | null>(null);
  protected readonly falhaAoRemover = signal<string | null>(null);

  protected readonly equipe = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.equipe : null;
  });

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected status(convite: OrganizationInvitation): { rotulo: string; tom: BadgeTone } {
    return STATUS[convite.status];
  }

  /** Recarrega sem voltar ao estado de carregamento quando a equipe já está na tela. */
  protected carregar(): void {
    if (this.estado().tipo !== 'pronto') {
      this.estado.set({ tipo: 'carregando' });
    }

    this.service.team(this.organizacao()).subscribe({
      next: (equipe) => this.estado.set({ tipo: 'pronto', equipe }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected convidar(event: Event): void {
    event.preventDefault();
    this.retorno.set(null);

    const email = this.email().trim();
    if (!EMAIL_VALIDO.test(email) || email.length > EMAIL_MAX) {
      this.erroDoEmail.set('Informe um e-mail válido, como nome@exemplo.com.');
      return;
    }

    this.erroDoEmail.set(null);
    this.convidando.set(true);

    this.service.invite(this.organizacao(), email).subscribe({
      next: () => {
        this.convidando.set(false);
        this.email.set('');
        this.retorno.set({ tom: 'success', texto: `Convite enviado para ${email}.` });
        // Convidar de novo o mesmo e-mail revoga o convite anterior no servidor; a lista
        // vem da API para refletir isso em vez de ser remontada aqui.
        this.carregar();
      },
      error: (falha: ApiFailure) => {
        this.convidando.set(false);
        this.retorno.set({ tom: 'danger', texto: falha.message });
      },
    });
  }

  protected pedirRevogacao(convite: OrganizationInvitation): void {
    this.falhaAoRevogar.set(null);
    this.revogando.set(convite);
  }

  protected cancelarRevogacao(): void {
    if (!this.confirmando()) {
      this.revogando.set(null);
    }
  }

  protected revogar(): void {
    const convite = this.revogando();
    if (!convite || this.confirmando()) {
      return;
    }

    this.confirmando.set(true);
    this.service.revoke(this.organizacao(), convite.id).subscribe({
      next: () => this.concluir(`Convite de ${convite.invitedEmail} revogado.`),
      error: (falha: ApiFailure) => {
        if (falha.status === 409) {
          // O convite foi aceito ou revogado enquanto a tela estava aberta.
          this.concluir(
            `O convite de ${convite.invitedEmail} já não estava pendente. A lista foi atualizada.`,
          );
          return;
        }

        this.confirmando.set(false);
        this.falhaAoRevogar.set(falha.message);
      },
    });
  }

  protected pedirRemocao(pessoa: OrganizationAssistant): void {
    this.falhaAoRemover.set(null);
    this.removendo.set(pessoa);
  }

  protected cancelarRemocao(): void {
    if (!this.confirmando()) {
      this.removendo.set(null);
    }
  }

  protected remover(): void {
    const pessoa = this.removendo();
    if (!pessoa || this.confirmando()) {
      return;
    }

    this.confirmando.set(true);
    this.service.removeAssistant(this.organizacao(), pessoa.userId).subscribe({
      next: () => this.concluir(`${pessoa.displayName} saiu da equipe.`),
      error: (falha: ApiFailure) => {
        if (falha.status === 404) {
          // Outra aba ou outra pessoa já removeu; o resultado é o mesmo.
          this.concluir(
            `${pessoa.displayName} já não fazia parte da equipe. A lista foi atualizada.`,
          );
          return;
        }

        this.confirmando.set(false);
        this.falhaAoRemover.set(falha.message);
      },
    });
  }

  private concluir(texto: string): void {
    this.confirmando.set(false);
    this.revogando.set(null);
    this.removendo.set(null);
    this.retorno.set({ tom: 'success', texto });
    this.carregar();

    // O botão de origem some da lista; o foco vai para o aviso do resultado em vez de
    // cair no início da página.
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }
}
