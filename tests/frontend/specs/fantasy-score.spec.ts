import { expect, test } from '@playwright/test';

import {
  ApiDaSessao,
  horarioDeBrasilia,
  lancarSumulaDaRodada,
  publicarCampeonatoDemo,
} from '../support/campeonato';
import { ADMIN_E2E_EMAIL, entrarComContaNova, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

/** Elenco do Fut7 por posição: titulares da formação mais um reserva de cada. */
const ELENCO_FUT7 = { Goalkeeper: 2, Defender: 3, Midfielder: 3, Forward: 3 };

test('a jornada inteira: a escalação congela, a rodada é publicada e o participante vê os pontos', async ({
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
  await contexto.close();
});

/** Adere e compra o elenco mais barato que o servidor libera; devolve o capitão. */
async function montarElencoPelaApi(api: ApiDaSessao, slug: string): Promise<string> {
  type Item = {
    kind: string;
    id: string;
    position: string | null;
    price: number;
    blockCode: string | null;
  };
  await api.post(`/api/v1/fantasy/${slug}/entry`);
  const posicoes: (string | null)[] = [
    ...Object.entries(ELENCO_FUT7).flatMap(([posicao, quantidade]) =>
      Array.from({ length: quantidade }, () => posicao),
    ),
    null,
  ];
  for (const posicao of posicoes) {
    const { items } = await api.get<{ items: Item[] }>(`/api/v1/fantasy/${slug}/market`);
    const escolha = items
      .filter((item) => item.position === posicao && item.blockCode === null)
      .sort((a, b) => a.price - b.price)[0]!;
    await api.post(
      `/api/v1/fantasy/${slug}/squad/${escolha.kind === 'Coach' ? 'tecnico' : 'atleta'}/${escolha.id}`,
    );
  }

  const visao = await api.get<{
    entry: { slots: { assetId: string; name: string; position: string; role: string }[] };
  }>(`/api/v1/fantasy/${slug}/`);
  const capitao = visao.entry.slots.find(
    (slot) => slot.position === 'Forward' && slot.role === 'Starter',
  )!;
  await api.put(`/api/v1/fantasy/${slug}/lineup/captain`, { athleteId: capitao.assetId });
  return capitao.name;
}
