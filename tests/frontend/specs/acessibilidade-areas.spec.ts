import { Page, expect, test } from '@playwright/test';

import { focoSempreVisivel, semViolacoes } from '../support/acessibilidade';
import {
  ApiDaSessao,
  horarioDeBrasilia,
  montarElencoPelaApi,
  publicarCampeonatoDemo,
} from '../support/campeonato';
import { ADMIN_E2E_EMAIL, entrarComContaNova, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

/**
 * Varredura das áreas com conta (Fase 8, 06 Parte 4): o jogo de quem participa e a
 * organização de quem organiza, que até aqui só eram medidas pelas telas públicas.
 *
 * Cada tela passa pelo axe na viewport do projeto e, depois, é aberta a 320 px — a
 * largura em que a revisão responsiva achou o início da conta nova, o Meu time e as
 * Importações rolando para o lado. A pontuação da rodada é varrida na jornada que já
 * publica uma rodada (fantasy-score), para não esperar o relógio duas vezes.
 */
test('as telas do jogo e da organização passam no axe e cabem em 320 px', async ({
  browser,
  page,
  request,
}) => {
  test.setTimeout(4 * 60_000);

  const contextoAdmin = await browser.newContext();
  const admin = await contextoAdmin.newPage();
  const ehAdmin = await entrarComoAdmin(admin);
  test.skip(
    !ehAdmin && !process.env['CI'],
    `A API local em execução não tem ${ADMIN_E2E_EMAIL} como administrador inicial.`,
  );
  const { organizacao } = await criarOrganizacaoAprovada(page, admin, request);

  // Mercado aberto por dez dias: nada aqui depende do relógio.
  const daqui = new Date(Date.now() + 10 * 24 * 3_600_000);
  const { slug, competitionId } = await publicarCampeonatoDemo(
    page,
    organizacao,
    `Copa Acessível ${Date.now()}`,
    horarioDeBrasilia(daqui),
  );

  const jogador = await (await browser.newContext()).newPage();
  await entrarComContaNova(jogador, request, 'Pessoa Acessível');
  const deJogador = new ApiDaSessao(jogador);

  // Antes de entrar em campeonato: o primeiro passo da conta nova.
  await varrer(jogador, '/inicio', 'início da conta nova');

  await montarElencoPelaApi(deJogador, slug);
  const liga = await deJogador.post<{ id: string }>(`/api/v1/fantasy/${slug}/leagues`, {
    name: 'Liga da Varredura',
  });

  // A casca mobile mantém rodada e mercado visíveis sem exigir a abertura de "Mais".
  const viewportDoProjeto = jogador.viewportSize()!;
  await jogador.setViewportSize({ width: 320, height: 720 });
  await jogador.goto(`/c/${slug}/mercado`);
  await expect(jogador.locator('.topo__jogo-mobile')).toBeVisible();
  await expect(jogador.locator('.topo__jogo-mobile')).toContainText(/Rodada/);
  await expect(
    jogador.locator('.topo__jogo-mobile [aria-label^="Status do mercado:"]'),
  ).toBeVisible();
  await jogador.setViewportSize(viewportDoProjeto);

  const doJogo: readonly (readonly [string, string])[] = [
    ['/inicio', 'início com campeonato'],
    [`/c/${slug}/escalacao`, 'meu time'],
    [`/c/${slug}/mercado`, 'mercado'],
    [`/c/${slug}/ranking`, 'classificação'],
    [`/c/${slug}/ligas/${liga.id}`, 'liga'],
    [`/c/${slug}/rodadas`, 'rodadas'],
    ['/perfil', 'conta'],
  ];
  for (const [endereco, nome] of doJogo) {
    await varrer(jogador, endereco, nome);
  }
  // A lista longa do mercado é onde o Tab levava o foco para baixo da barra inferior.
  await focoSempreVisivel(jogador, `/c/${slug}/mercado`, 'mercado');

  const area = `/organizar/c/${competitionId}`;
  const daOrganizacao: readonly (readonly [string, string])[] = [
    ['/organizar', 'minhas organizações'],
    [area, 'resumo do campeonato'],
    [`${area}/fases`, 'fases'],
    [`${area}/rodadas`, 'rodadas da organização'],
    [`${area}/times`, 'times'],
    [`${area}/atletas`, 'atletas'],
    [`${area}/tecnicos`, 'técnicos'],
    [`${area}/importacoes`, 'importações'],
    [`${area}/publicacao`, 'publicação'],
    [`${area}/configuracao`, 'configuração'],
  ];
  for (const [endereco, nome] of daOrganizacao) {
    await varrer(page, endereco, nome);
  }
  await focoSempreVisivel(page, `${area}/rodadas`, 'rodadas da organização');

  await varrer(admin, '/admin/solicitacoes', 'solicitações de organizador');
});

async function varrer(pagina: Page, endereco: string, nome: string): Promise<void> {
  await pagina.goto(endereco);
  // Sem esperar o conteúdo, o axe varreria o estado de carregamento.
  await expect(pagina.getByRole('heading', { level: 1 }).first()).toBeVisible();
  await expect(pagina.locator('app-loading')).toHaveCount(0);
  await semViolacoes(pagina, nome);

  const original = pagina.viewportSize()!;
  await pagina.setViewportSize({ width: 320, height: 720 });
  // A medida espera o layout assentar. Logo depois da troca de largura, uma transição da
  // casca ainda pode estar no meio do caminho e somar uma sobra que some sozinha — no
  // desktop isso reprovava uma tela diferente a cada execução. Sobra de verdade não some,
  // e continua reprovando.
  await expect
    .poll(
      () =>
        pagina.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        ),
      { message: `${nome} rola na horizontal a 320 px`, timeout: 2_000 },
    )
    .toBeLessThanOrEqual(0);
  await pagina.setViewportSize(original);
}
