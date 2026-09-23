import { Browser, APIRequestContext, Page, expect, test } from '@playwright/test';

import { ApiDaSessao, horarioDeBrasilia, publicarCampeonatoDemo } from '../support/campeonato';
import { ADMIN_E2E_EMAIL, entrarComContaNova, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

/** Vagas do Fut7: 7 titulares, 4 reservas e o técnico. */
const VAGAS_DO_FUT7 = 12;

/** Elenco de atletas por posição: titulares da formação mais um reserva de cada. */
const ELENCO_FUT7 = { Goalkeeper: 2, Defender: 3, Midfielder: 3, Forward: 3 };
const ELENCO_CAMPO = { Goalkeeper: 2, Defender: 5, Midfielder: 4, Forward: 4 };

test('participante monta a escalação pelo campo, escolhe o capitão e troca com o banco', async ({
  browser,
  page,
  request,
}) => {
  test.slow();
  const organizacao = await organizacaoAprovada(browser, page, request);
  const { slug } = await publicarCampeonatoDemo(
    page,
    organizacao,
    `Copa Campo ${Date.now()}`,
    horarioDeBrasilia(new Date(Date.now() + 10 * 24 * 3600 * 1000)),
  );

  const contexto = await browser.newContext();
  const jogador = await contexto.newPage();
  await entrarComContaNova(jogador, request, 'Pessoa Técnica');
  await jogador.goto(`/c/${slug}/jogar`);
  await jogador.getByRole('button', { name: 'Entrar no campeonato' }).click();
  await jogador.getByRole('link', { name: 'Escalar meu time' }).click();

  await expect(jogador.getByRole('heading', { name: 'Meu time', level: 1 })).toBeVisible();
  await expect(jogador.getByRole('heading', { name: 'Titulares 1-2-2-2' })).toBeVisible();
  await expect(jogador.getByRole('button', { name: /^Escolher / })).toHaveCount(VAGAS_DO_FUT7);
  await semRolagemHorizontal(jogador);

  // Cada vaga vazia abre a escolha filtrada pela posição no próprio campo: sem ida e
  // volta ao mercado, a página não muda.
  await jogador.getByRole('button', { name: 'Escolher goleiro titular' }).click();
  const escolha = jogador.getByRole('dialog', { name: 'Escolher goleiro titular' });
  await expect(escolha).toBeVisible();
  await semRolagemHorizontal(jogador);
  const goleiro = await comprarOMaisBarato(jogador);
  await expect(jogador).toHaveURL(new RegExp(`/c/${slug}/escalacao$`));
  await expect(jogador.getByText(`${goleiro} entrou como titular.`)).toBeVisible();
  await expect(
    jogador
      .getByRole('list', { name: 'Goleiro' })
      .getByRole('button', { name: new RegExp(goleiro) }),
  ).toBeVisible();

  for (let compra = 2; compra <= VAGAS_DO_FUT7; compra++) {
    await jogador
      .getByRole('button', { name: /^Escolher / })
      .first()
      .click();
    await comprarOMaisBarato(jogador);
    await expect(jogador.getByRole('button', { name: /^Escolher / })).toHaveCount(
      VAGAS_DO_FUT7 - compra,
    );
  }

  const resumo = jogador.getByRole('region', { name: 'Resumo da escalação' });
  await expect(resumo.getByText('Escolha o capitão entre os titulares.')).toBeVisible();
  await semRolagemHorizontal(jogador);

  // Capitão: pelo diálogo do atleta, sem arrastar nada.
  const atacante = jogador.getByRole('list', { name: 'Atacantes' }).getByRole('button').first();
  const nomeDoCapitao = (await atacante.locator('.ficha__nome').textContent())!.trim();
  await atacante.click();
  const acoes = jogador.getByRole('dialog', { name: nomeDoCapitao });
  await acoes.getByRole('button', { name: 'Tornar capitão' }).click();
  await expect(jogador.getByText(`${nomeDoCapitao} é o capitão.`)).toBeVisible();
  await expect(
    jogador.getByRole('button', { name: new RegExp(`${nomeDoCapitao}.*, capitão`) }),
  ).toBeVisible();
  await expect(resumo.getByText('Escalação completa. Ela vale para a Rodada 1')).toBeVisible();

  // O reserva do gol entra no lugar do titular, que vai para o banco.
  const reserva = jogador.getByRole('button', { name: /^Reserva de goleiro:/ });
  const nomeDoReserva = (await reserva.locator('.ficha__nome').textContent())!.trim();
  await reserva.click();
  await jogador
    .getByRole('dialog', { name: nomeDoReserva })
    .getByRole('button', { name: `Entrar no lugar de ${goleiro}` })
    .click();
  await expect(
    jogador.getByText(`${nomeDoReserva} entrou no lugar de ${goleiro}, que foi para o banco.`),
  ).toBeVisible();
  await expect(
    jogador.getByRole('list', { name: 'Goleiro' }).getByRole('button', {
      name: new RegExp(nomeDoReserva),
    }),
  ).toBeVisible();

  // Tudo já está no servidor: não existe botão de salvar.
  await jogador.reload();
  await expect(resumo.getByText('Escalação completa.')).toBeVisible();
  await expect(jogador.getByRole('button', { name: /^Reserva de goleiro:/ })).toContainText(
    goleiro,
  );
  await contexto.close();
});

test('a escalação completa congela quando o mercado fecha pelo relógio do servidor', async ({
  browser,
  page,
  request,
}, testInfo) => {
  test.skip(
    testInfo.project.name !== 'chromium-mobile',
    'O critério de saída da Fase 9 pede o congelamento em viewport móvel; um projeto basta.',
  );
  test.setTimeout(6 * 60_000);

  const organizacao = await organizacaoAprovada(browser, page, request);
  const contexto = await browser.newContext();
  const jogador = await contexto.newPage();
  expect(jogador.viewportSize()!.width, 'O contexto novo herda o celular do projeto').toBeLessThan(
    500,
  );
  await entrarComContaNova(jogador, request, 'Pessoa Apressada');

  // O mercado fecha na primeira partida, marcada para o próximo minuto cheio depois de 75 s.
  const fechamento = new Date(Math.ceil((Date.now() + 75_000) / 60_000) * 60_000);
  const { slug } = await publicarCampeonatoDemo(
    page,
    organizacao,
    `Copa Relógio ${Date.now()}`,
    horarioDeBrasilia(fechamento),
  );

  const api = new ApiDaSessao(jogador);
  await jogador.goto(`/c/${slug}/escalacao`);
  const capitao = await montarElencoPelaApi(api, slug, ELENCO_FUT7);

  await jogador.reload();
  const resumo = jogador.getByRole('region', { name: 'Resumo da escalação' });
  await expect(resumo.getByText('Escalação completa. Ela vale para a Rodada 1')).toBeVisible();
  await expect(jogador.getByText(/Faltam \d/)).toBeVisible();

  // Ninguém recarrega a página: a contagem chega a zero e a tela pergunta ao servidor.
  await expect(jogador.getByText('Escalação congelada para a Rodada 1 desde')).toBeVisible({
    timeout: fechamento.getTime() - Date.now() + 60_000,
  });
  await expect(jogador.getByText('Mercado fechado')).toBeVisible();
  await expect(jogador.getByRole('button', { name: /^Escolher / })).toHaveCount(0);
  await expect(
    jogador.getByRole('region', { name: 'Titulares 1-2-2-2' }).getByRole('button'),
  ).toHaveCount(0);
  await expect(
    jogador.getByText(new RegExp(`Atacante titular: ${capitao}, .*, capitão`)),
  ).toHaveCount(1);
  await semRolagemHorizontal(jogador);

  // Com a tela ignorada, o servidor recusa mexer na escalação congelada.
  const recusa = await api.putBruto(`/api/v1/fantasy/${slug}/lineup/captain`, {
    athleteId: '00000000-0000-0000-0000-000000000001',
  });
  expect(recusa.status()).toBe(409);
  expect(((await recusa.json()) as { code: string }).code).toBe('fantasy_market_closed');
  await contexto.close();
});

test('no futebol de campo, os 11 titulares cabem em 360 px sem encolher o alvo de toque', async ({
  browser,
  page,
  request,
}, testInfo) => {
  test.skip(
    testInfo.project.name !== 'chromium-mobile',
    'É um teste da tela mais estreita (01 §15); um projeto basta.',
  );
  test.slow();

  const organizacao = await organizacaoAprovada(browser, page, request);
  const { slug } = await publicarCampeonatoDemo(
    page,
    organizacao,
    `Copa Gramado ${Date.now()}`,
    horarioDeBrasilia(new Date(Date.now() + 10 * 24 * 3600 * 1000)),
    'Field',
  );

  const contexto = await browser.newContext({ viewport: { width: 360, height: 780 } });
  const jogador = await contexto.newPage();
  await entrarComContaNova(jogador, request, 'Pessoa do Gramado');
  await jogador.goto(`/c/${slug}/escalacao`);
  await montarElencoPelaApi(new ApiDaSessao(jogador), slug, ELENCO_CAMPO);
  await jogador.reload();

  const titulares = jogador.getByRole('region', { name: 'Titulares 1-4-3-3' });
  await expect(titulares.getByRole('list', { name: 'Defensores' }).getByRole('button')).toHaveCount(
    4,
  );
  await expect(titulares.getByRole('button')).toHaveCount(11);
  await expect(
    jogador.getByRole('region', { name: 'Resumo da escalação' }).getByText('Escalação completa.'),
  ).toBeVisible();
  await semRolagemHorizontal(jogador);

  // A vaga estreita na largura, mas nunca fica abaixo do alvo de toque mínimo (02 §6).
  for (const vaga of await jogador.locator('.ficha').all()) {
    const caixa = (await vaga.boundingBox())!;
    expect(caixa.width, 'Vaga mais estreita que 44 px').toBeGreaterThanOrEqual(44);
    expect(caixa.height, 'Vaga mais baixa que 44 px').toBeGreaterThanOrEqual(44);
  }
  await contexto.close();
});

/** Organização aprovada pela fila da administração; `page` termina logada como dona. */
async function organizacaoAprovada(
  browser: Browser,
  page: Page,
  request: APIRequestContext,
): Promise<string> {
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
  return organizacao;
}

/**
 * Na escolha aberta pela vaga, compra o liberado mais barato — o orçamento de C$ 100
 * precisa fechar as 12 vagas — e devolve o nome de quem entrou. A lista já vem do mais
 * barato para o mais caro, com quem está bloqueado no fim.
 */
async function comprarOMaisBarato(jogador: Page): Promise<string> {
  const escolha = jogador.getByRole('dialog');
  const comprar = escolha.getByRole('button', { name: /^Comprar /, disabled: false }).first();
  await expect(comprar).toBeVisible();
  const nome = ((await comprar.textContent()) ?? '').replace(/^\s*Comprar\s*/, '').trim();
  await comprar.click();
  await expect(escolha).toBeHidden();
  return nome;
}

/**
 * Adere e compra o elenco mais barato que o servidor libera, com o capitão num atacante.
 * Devolve o nome do capitão.
 */
async function montarElencoPelaApi(
  api: ApiDaSessao,
  slug: string,
  elenco: Readonly<Record<string, number>>,
): Promise<string> {
  type Item = {
    kind: string;
    id: string;
    name: string;
    position: string | null;
    price: number;
    blockCode: string | null;
  };
  await api.post(`/api/v1/fantasy/${slug}/entry`);
  const posicoes: (string | null)[] = [
    ...Object.entries(elenco).flatMap(([posicao, quantidade]) =>
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

async function semRolagemHorizontal(page: Page): Promise<void> {
  const sobra = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(sobra, 'A página rola na horizontal').toBeLessThanOrEqual(0);
}
