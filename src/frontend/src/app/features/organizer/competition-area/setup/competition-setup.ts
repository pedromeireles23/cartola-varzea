import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  ActivatedRoute,
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { Check, type IconNode } from 'lucide';
import { filter } from 'rxjs';

import { Alert, Card, FailureState, Icon, Loading } from '../../../../shared/ui';
import { CompetitionContext } from '../competition-context';
import { SETUP_STEPS, SETUP_STEP_LABELS, SetupStep, SetupStore } from './setup-store';

/**
 * Montagem do campeonato em passos (`/organizar/c/:campeonato/montar`).
 *
 * Times, elencos, formato, tabela e publicação, na ordem em que um depende do outro.
 * Cada passo grava pelo caminho de sempre — a planilha deixa de ser necessária, mas as
 * regras são as mesmas —, e o estado dos passos sai dos dados do campeonato. As telas de
 * Times, Atletas, Fases e Rodadas continuam para o ajuste fino.
 */
@Component({
  selector: 'app-competition-setup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Card, FailureState, Icon, Loading, RouterLink, RouterLinkActive, RouterOutlet],
  providers: [SetupStore],
  template: `
    <div class="pagina">
      <h1>Montar campeonato</h1>
      <p class="intro">
        Cinco passos, na ordem em que o campeonato precisa. Dá para sair e voltar quando quiser: o
        que foi salvo fica salvo.
      </p>

      @if (criado()) {
        <app-alert tone="success">
          Campeonato criado em rascunho. Comece pelos times: só a organização vê até a publicação.
        </app-alert>
      }

      <nav class="passos" aria-label="Passos da montagem">
        <ol>
          @for (passo of passos(); track passo.step; let numero = $index) {
            <li>
              <a
                class="passo"
                [class.passo--feito]="passo.done"
                [routerLink]="passo.step"
                routerLinkActive="passo--atual"
                ariaCurrentWhenActive="step"
              >
                <span class="passo__numero" aria-hidden="true">
                  @if (passo.done) {
                    <svg [appIcon]="feito" [size]="16" />
                  } @else {
                    {{ numero + 1 }}
                  }
                </span>
                <span class="passo__texto">
                  <span class="passo__rotulo">{{ numero + 1 }}. {{ passo.label }}</span>
                  <span class="passo__detalhe">
                    @if (passo.done) {
                      <span class="sr-only">pronto: </span>
                    }
                    {{ passo.detail }}
                  </span>
                </span>
              </a>
            </li>
          }
        </ol>
      </nav>

      @switch (store.estado().tipo) {
        @case ('carregando') {
          <app-card><app-loading label="Abrindo a montagem…" skeleton="lines" /></app-card>
        }
        @case ('erro') {
          <app-card>
            <app-failure-state [failure]="store.falha()!" (retry)="store.load()" />
          </app-card>
        }
        @case ('pronto') {
          <router-outlet />
        }
      }
    </div>
  `,
  styleUrls: ['../../organizer.scss', '../competition-pages.scss', './setup.scss'],
})
export class CompetitionSetupPage {
  protected readonly store = inject(SetupStore);
  private readonly contexto = inject(CompetitionContext);

  protected readonly feito: IconNode = Check;
  protected readonly criado = signal(this.contexto.consumirCriado());

  /** Antes de carregar, os passos aparecem sem estado, para a navegação já existir. */
  protected readonly passos = computed(() => {
    const carregados = this.store.passos();
    if (carregados.length > 0) return carregados;
    return SETUP_STEPS.map((step) => ({
      step,
      label: SETUP_STEP_LABELS[step],
      done: false,
      detail: '…',
    }));
  });

  constructor() {
    this.store.load();

    // O aviso da criação vale para o começo da montagem; nos passos seguintes, sai.
    inject(Router)
      .events.pipe(
        filter((evento) => evento instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((evento) => {
        if (!/\/montar(\/times)?$/.test(evento.urlAfterRedirects)) this.criado.set(false);
      });
  }
}

/**
 * `/montar` sem passo leva ao primeiro que falta, depois de saber qual é. Fica numa
 * rota própria para o endereço de cada passo continuar estável e compartilhável.
 */
@Component({
  selector: 'app-setup-start',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
export class SetupStart {
  private readonly store = inject(SetupStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  constructor() {
    effect(() => {
      if (this.store.dados()) {
        void this.router.navigate([this.store.primeiroPendente()], {
          relativeTo: this.route,
          replaceUrl: true,
        });
      }
    });
  }
}

/** Voltar e seguir no fim de cada passo, com o nome do próximo à vista. */
@Component({
  selector: 'app-setup-footer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <nav class="rodape-passo" aria-label="Seguir na montagem">
      @if (anterior(); as passo) {
        <a class="acao" [routerLink]="['..', passo]">Voltar: {{ rotulo(passo) }}</a>
      }
      @if (proximo(); as passo) {
        <a class="acao acao--principal" [routerLink]="['..', passo]">
          Próximo: {{ rotulo(passo) }}
        </a>
      }
    </nav>
  `,
  styleUrls: ['../../organizer.scss', '../competition-pages.scss', './setup.scss'],
})
export class SetupFooter {
  readonly passo = input.required<SetupStep>();

  protected readonly anterior = computed(
    () => SETUP_STEPS[SETUP_STEPS.indexOf(this.passo()) - 1] ?? null,
  );
  protected readonly proximo = computed(
    () => SETUP_STEPS[SETUP_STEPS.indexOf(this.passo()) + 1] ?? null,
  );

  protected rotulo(passo: SetupStep): string {
    return SETUP_STEP_LABELS[passo];
  }
}
