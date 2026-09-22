import { expect, test } from '@playwright/test';

import {
  ApiDaSessao,
  horarioDeBrasilia,
  lancarSumulaDaRodada,
  montarElencoPelaApi,
  publicarCampeonatoDemo,
} from '../support/campeonato';
import { ADMIN_E2E_EMAIL, entrarComContaNova, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

test('a jornada inteira: a escalação congela, a rodada sai, é corrigida e o participante acompanha', async ({
  browser,
  page,
  request,
}, testInfo) => {
  test.skip(
    testInfo.project.name !== 'chromium-mobile',
    'A jornada depende do relógio real; um projeto basta.',
  );
  test.setTimeout(7 * 60_000);

  const contextoAdmin = await browser.newContext();
  const admin = await contextoAdmin.newPage();
  const ehAdmin = await entrarComoAdmin(admin);
  test.skip(
    !ehAdmin && !process.env['CI'],
    `A API local em execução não tem ${ADMIN_E2E_EMAIL} como administrador inicial.`,
  );
  expect(ehAdmin, 'A conta do E2E deveria ser Platform admin').toBe(true);
  const { organizacao } = await criarOrganizacaoAprovada(page, admin, request);
  await contextoAdmin.close();

  // A conta de quem joga nasce antes do relógio começar a correr.
  const contexto = await browser.newContext();
  const jogador = await contexto.newPage();
  await entrarComContaNova(jogador, request, 'Pessoa Pontuada');

  // O mercado fecha na primeira partida, no próximo minuto cheio depois de 75 s.
  const fechamento = new Date(Math.ceil((Date.now() + 75_000) / 60_000) * 60_000);
  const { slug, competitionId, roundId } = await publicarCampeonatoDemo(
    page,
    organizacao,
    `Copa Apuração ${Date.now()}`,
    horarioDeBrasilia(fechamento),
  );

  const doJogador = new ApiDaSessao(jogador);
  await jogador.goto(`/c/${slug}/escalacao`);
  const capitao = await montarElencoPelaApi(doJogador, slug);
  await jogador.reload();
  await expect(jogador.getByText('Escalação congelada para a Rodada 1 desde')).toBeVisible({
    timeout: fechamento.getTime() - Date.now() + 60_000,
  });

  // Com o jogo encerrado, a organização lança a súmula e manda para revisão.
  const artilheiro = await lancarSumulaDaRodada(new ApiDaSessao(page), competitionId, roundId);

  // Publicar é pela tela, que é o caminho do organizador.
  await page.goto(`/organizar/c/${competitionId}/rodadas/${roundId}/revisao`);
  await page.getByRole('button', { name: 'Publicar resultado' }).click();
  const confirmacao = page.getByRole('dialog', { name: 'Publicar o resultado de Rodada 1?' });
  await confirmacao.getByRole('button', { name: 'Publicar', exact: true }).click();
  await expect(page.getByText('Resultado publicado.')).toBeVisible();
  await expect(page.getByText('Resultado provisório até')).toBeVisible();
  await expect(page.getByText(/Participações apuradas\s*1/)).toBeVisible();

  // O sino avisa que saiu resultado, e o aviso leva à pontuação da rodada.
  await jogador.goto(`/c/${slug}/jogar`);
  const sino = jogador.getByRole('button', { name: 'Avisos, 1 por ler' });
  await expect(sino).toBeVisible();
  await sino.click();
  await expect(jogador.getByRole('link', { name: 'Rodada 1 apurada' })).toBeVisible();
  await jogador.keyboard.press('Escape');
  await expect(jogador.getByRole('button', { name: 'Avisos', exact: true })).toBeVisible();

  // E quem jogou lê a própria pontuação, com o detalhamento.
  await jogador.goto(`/c/${slug}/jogar`);
  await expect(jogador.getByText('Rodadas apuradas')).toBeVisible();
  await jogador.getByRole('link', { name: 'Rodada 1' }).click();

  await expect(jogador.getByRole('heading', { name: 'Rodada 1', level: 1 })).toBeVisible();
  await expect(jogador.getByText('Resultado provisório: pode mudar até')).toBeVisible();
  await expect(jogador.getByText('Sua pontuação na rodada')).toBeVisible();
  await expect(jogador.getByText(`${capitao}`).first()).toBeVisible();
  await expect(jogador.getByText('Capitão').first()).toBeVisible();

  // O goleiro do mandante não sofreu gol: a linha do bônus aparece no detalhamento.
  const bonus = jogador.getByText('1 jogo sem sofrer gol');
  const gol = jogador.getByText('1 gol', { exact: false });
  expect(
    (await bonus.count()) + (await gol.count()),
    `Nenhum evento apareceu no detalhamento (artilheiro: ${artilheiro})`,
  ).toBeGreaterThan(0);

  const sobra = await jogador.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(sobra, 'A página rola na horizontal').toBeLessThanOrEqual(0);

  // A correção: a liga reabre a rodada, refaz a súmula e republica.
  const totalAntes = await jogador.locator('.total__valor').innerText();

  await page.goto(`/organizar/c/${competitionId}/rodadas/${roundId}/revisao`);
  await page.getByRole('button', { name: 'Reabrir para correção' }).click();
  const reabertura = page.getByRole('dialog', { name: 'Reabrir Rodada 1 para correção?' });
  await reabertura
    .getByLabel('Motivo da correção')
    .fill('A súmula chegou com um gol a menos do mandante.');
  await reabertura.getByRole('button', { name: 'Reabrir', exact: true }).click();
  await expect(page.getByText('Rodada reaberta.')).toBeVisible();
  await expect(page.getByText('Rodada reaberta em')).toBeVisible();

  // Enquanto isso, quem joga continua lendo o resultado que está valendo.
  await jogador.reload();
  await expect(jogador.getByText('A liga reabriu esta rodada para correção')).toBeVisible();
  await expect(jogador.locator('.total__valor')).toHaveText(totalAntes);

  await lancarSumulaDaRodada(new ApiDaSessao(page), competitionId, roundId, 2);
  await page.reload();
  await page.getByRole('button', { name: 'Republicar resultado' }).click();
  const republicacao = page.getByRole('dialog', { name: 'Republicar o resultado de Rodada 1?' });
  await republicacao.getByRole('button', { name: 'Republicar', exact: true }).click();
  await expect(page.getByText('Resultado corrigido.')).toBeVisible();
  await expect(page.getByText(/Revisão da apuração\s*2ª/)).toBeVisible();

  // E lê o que mudou, com o total de antes ao lado do de agora.
  await jogador.reload();
  await expect(jogador.getByText('Rodada corrigida em')).toBeVisible();
  await expect(
    jogador.getByText('Motivo: A súmula chegou com um gol a menos do mandante.'),
  ).toBeVisible();
  await expect(jogador.getByText(/Sua pontuação (foi de|não mudou)/)).toBeVisible();
  await expect(jogador.getByText('A liga reabriu esta rodada')).toBeHidden();

  await contexto.close();
});
