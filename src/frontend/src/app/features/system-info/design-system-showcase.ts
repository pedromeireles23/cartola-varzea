import { ChangeDetectionStrategy, Component } from '@angular/core';
import { CircleCheck, Clock, Save, ShieldCheck, Trophy, TriangleAlert } from 'lucide';

import {
  Badge,
  Button,
  Card,
  EmptyState,
  FailureState,
  Icon,
  Loading,
  PageHeader,
  Panel,
} from '../../shared/ui';
import { ApiFailure } from '../../core/api/problem-details';

/**
 * Laboratório isolado da direção visual V1.
 *
 * Não participa das rotas de produto: mantém os contextos do jogador e do organizador
 * lado a lado para validar o sistema antes da migração das telas reais.
 */
@Component({
  selector: 'app-design-system-showcase',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Badge, Button, Card, EmptyState, FailureState, Icon, Loading, PageHeader, Panel],
  template: `
    <main class="showcase" data-testid="design-system-showcase">
      <section class="theme-player" data-testid="theme-player" aria-label="Tema do jogador">
        <app-page-header
          class="showcase__intro"
          data-testid="header-default"
          variant="default"
          [headingLevel]="1"
          kicker="Laboratório V1"
          heading="Noite de jogo"
          subtitle="Componentes do fantasy em um contexto esportivo e sem mecânicas de aposta."
        >
          <app-badge tone="info">Tema jogador</app-badge>
        </app-page-header>

        <app-page-header
          class="showcase__page-header showcase__page-header--game"
          data-testid="header-game"
          variant="game"
          [headingLevel]="2"
          kicker="Copa Jardim União · Rodada 5"
          heading="Seu time entra em campo"
          subtitle="Mercado aberto até sábado, 13:30"
        >
          <app-button variant="secondary" size="compact">
            <svg [appIcon]="icons.clock" [size]="18" />Ver confrontos
          </app-button>
          <app-button size="regular">
            <svg [appIcon]="icons.trophy" [size]="18" />Completar meu time
          </app-button>
        </app-page-header>

        <div class="showcase__grid">
          <app-card
            class="showcase__score-card"
            data-testid="score-card"
            variant="highlight"
            heading="Placar da rodada"
            [headingLevel]="2"
          >
            <div class="match-score" aria-label="Bela Vista 2, União da Ponte 1">
              <span class="match-score__team"><span class="crest">BV</span>Bela Vista</span>
              <strong class="score match-score__number">2</strong>
              <span class="match-score__separator" aria-hidden="true">×</span>
              <strong class="score match-score__number">1</strong>
              <span class="match-score__team match-score__team--away">
                <span class="crest crest--away">UP</span>União da Ponte
              </span>
            </div>
            <div class="showcase__row">
              <app-badge tone="live">
                <span class="live-dot" aria-hidden="true"></span>Ao vivo
              </app-badge>
              <span class="showcase__clock">
                <span class="round-clock">18:42</span>
                <span class="showcase__muted">2º tempo</span>
              </span>
            </div>
          </app-card>

          <app-card variant="elevated" heading="Números em destaque" [headingLevel]="3">
            <dl class="metrics">
              <div class="metric">
                <dt>Pontos</dt>
                <dd class="score score--sm">87,6</dd>
              </div>
              <div class="metric">
                <dt>Posição</dt>
                <dd class="position-number">12º</dd>
              </div>
              <div class="metric">
                <dt>Escalados</dt>
                <dd class="score score--sm">7/7</dd>
              </div>
            </dl>
            <div class="motion-sample" data-testid="motion-sample">
              <span class="motion-sample__signal" aria-hidden="true"></span>
              <svg [appIcon]="icons.check" [size]="20" />
              <span>
                <strong>Feedback em 160 ms</strong>
                <small>O movimento some quando a pessoa prefere redução.</small>
              </span>
            </div>
          </app-card>

          <app-card variant="default" heading="Ações e estados" [headingLevel]="3">
            <div class="showcase__badges" aria-label="Exemplos de status">
              <app-badge tone="neutral">Reserva</app-badge>
              <app-badge tone="brand">Capitão</app-badge>
              <app-badge tone="success">Confirmado</app-badge>
              <app-badge tone="warning">Atenção</app-badge>
              <app-badge tone="danger">Indisponível</app-badge>
              <app-badge tone="info">Informação</app-badge>
            </div>
            <div class="showcase__actions">
              <app-button size="regular">Salvar escalação</app-button>
              <app-button variant="secondary" size="compact">Comparar atletas</app-button>
              <app-button variant="ghost" size="compact">Limpar filtros</app-button>
            </div>
          </app-card>

          <app-card variant="compact" heading="Destaque do mercado" [headingLevel]="3">
            <div class="athlete">
              <span class="athlete__avatar" aria-hidden="true">BR</span>
              <span class="athlete__identity">
                <strong>Breno “Canhota” Reis</strong>
                <small>Bela Vista · Ala</small>
              </span>
              <span class="athlete__points"><strong>15,8</strong><small>pts</small></span>
            </div>
          </app-card>

          <app-card variant="compact" heading="Estados vazios" [headingLevel]="3">
            <app-empty-state illustration="stands" heading="Você ainda não está em nenhuma liga">
              Crie a sua ou entre com o código que recebeu.
            </app-empty-state>
            <app-empty-state illustration="tactics" [compact]="true">
              Nenhum atleta corresponde a esses filtros.
            </app-empty-state>
          </app-card>

          <app-card variant="compact" heading="Carregando" [headingLevel]="3">
            <app-loading label="Abrindo o mercado…" skeleton="cards" />
          </app-card>
        </div>
      </section>

      <section
        class="theme-organizer"
        data-testid="theme-organizer"
        aria-label="Tema operacional do organizador"
      >
        <app-page-header
          class="showcase__page-header showcase__page-header--operational"
          data-testid="header-operational"
          variant="operational"
          [headingLevel]="2"
          kicker="Central da rodada"
          heading="Revisão das súmulas"
          subtitle="3 partidas · 1 pendência antes da publicação"
        >
          <app-button variant="secondary" size="compact">
            <svg [appIcon]="icons.shield" [size]="18" />Validar dados
          </app-button>
          <app-button size="compact">
            <svg [appIcon]="icons.save" [size]="18" />Salvar rascunho
          </app-button>
        </app-page-header>

        <div class="showcase__operational-grid">
          <app-panel
            data-testid="operational-panel"
            variant="operational"
            heading="Prontidão da rodada"
            supportingText="Atualizada há 2 minutos"
            [headingLevel]="3"
          >
            <app-badge panel-actions tone="warning">1 pendência</app-badge>
            <ul class="review-list">
              <li class="review-list__item">
                <span class="review-list__text">
                  <strong>Bela Vista × União da Ponte</strong>
                  <small>Placar e eventos conferidos</small>
                </span>
                <app-badge tone="success">Pronta</app-badge>
              </li>
              <li class="review-list__item">
                <span class="review-list__text">
                  <strong>Resenha 013 × Atlético do Parque</strong>
                  <small>Falta confirmar um cartão amarelo</small>
                </span>
                <app-badge tone="warning">Revisar</app-badge>
              </li>
              <li class="review-list__item">
                <span class="review-list__text">
                  <strong>Vila Nova 7 × Estrela do Norte</strong>
                  <small>Súmula recebida, sem divergências</small>
                </span>
                <app-badge tone="info">Recebida</app-badge>
              </li>
            </ul>
            <div panel-footer class="showcase__panel-footer">
              <span class="showcase__muted">A publicação libera a pontuação da rodada.</span>
              <app-button size="regular">Revisar rodada</app-button>
            </div>
          </app-panel>

          <app-card variant="compact" heading="Estados operacionais" [headingLevel]="3">
            <div class="showcase__status-list">
              <span><svg [appIcon]="icons.check" [size]="18" />Dados consistentes</span>
              <span><svg [appIcon]="icons.alert" [size]="18" />Pendência identificada</span>
              <span><svg [appIcon]="icons.clock" [size]="18" />Aguardando conferência</span>
            </div>
            <div class="showcase__actions">
              <app-button variant="secondary" size="compact">Exportar resumo</app-button>
              <app-button variant="destructive" size="compact">Cancelar rodada</app-button>
              <app-button variant="danger" size="compact">Reabrir rodada</app-button>
            </div>
          </app-card>

          <app-card variant="compact" heading="Estados vazios" [headingLevel]="3">
            <app-empty-state illustration="pitch" heading="Nenhuma rodada ainda">
              Crie a primeira para marcar os jogos.
            </app-empty-state>
            <app-empty-state illustration="scoreboard" [compact]="true">
              Esta súmula ainda não está disponível.
            </app-empty-state>
          </app-card>

          <app-card variant="compact" heading="Falha ao carregar" [headingLevel]="3">
            <app-loading label="Buscando as rodadas…" skeleton="lines" />
            <app-failure-state [failure]="falhaDeExemplo" />
          </app-card>
        </div>
      </section>
    </main>
  `,
  styleUrl: './design-system-showcase.scss',
})
export class DesignSystemShowcasePage {
  protected readonly falhaDeExemplo: ApiFailure = {
    status: 503,
    message: 'Não foi possível buscar as rodadas agora.',
    traceId: '00-vitrine-01',
  };

  protected readonly icons = {
    alert: TriangleAlert,
    check: CircleCheck,
    clock: Clock,
    save: Save,
    shield: ShieldCheck,
    trophy: Trophy,
  } as const;
}
