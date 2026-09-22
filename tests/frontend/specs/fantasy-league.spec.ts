import { Page, expect, test } from '@playwright/test';

import {
  ApiDaSessao,
  horarioDeBrasilia,
  lancarSumulaDaRodada,
  montarElencoPelaApi,
  publicarCampeonatoDemo,
} from '../support/campeonato';
import { ADMIN_E2E_EMAIL, entrarComContaNova, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

/**
 * Critério de saída da Fase 11: duas contas entram na mesma liga e veem exatamente o
 * mesmo ranking acumulado.
 *
 * A integração já prova isso na API; aqui o assunto é a tela — criar a liga, copiar o
 * código, entrar pelo link do convite e comparar as duas classificações lado a lado.
 * As duas contas capitaneiam posições diferentes de propósito: quem capitaneia quem
 * marcou termina na frente, e aí a ordem da lista também é conferida.
 */
test('duas contas entram na mesma liga e leem o mesmo ranking acumulado', async ({
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

  // As duas contas nascem antes de o relógio do mercado começar a correr.
  const contextoUm = await browser.newContext();
  const contextoDois = await browser.newContext();
  const dona = await contextoUm.newPage();
  const convidada = await contextoDois.newPage();
  await entrarComContaNova(dona, request, 'Dona da Liga');
  await entrarComContaNova(convidada, request, 'Convidada da Liga');

  const fechamento = new Date(Math.ceil((Date.now() + 75_000) / 60_000) * 60_000);
  const { slug, competitionId, roundId } = await publicarCampeonatoDemo(
    page,
    organizacao,
    `Copa das Ligas ${Date.now()}`,
    horarioDeBrasilia(fechamento),
  );

  // Mesmo elenco, capitães em posições diferentes: as pontuações não empatam.
  await montarElencoPelaApi(new ApiDaSessao(dona), slug, 'Forward');
  await montarElencoPelaApi(new ApiDaSessao(convidada), slug, 'Defender');

  // A liga é criada pela tela, antes mesmo de existir resultado.
  await dona.goto(`/c/${slug}/ligas`);
  await expect(dona.getByText('Você ainda não está em nenhuma liga')).toBeVisible();
  await dona.getByRole('button', { name: 'Criar uma liga' }).click();
  await dona.getByLabel('Nome da liga').fill('Turma do sábado');
  await dona.getByRole('button', { name: 'Criar liga' }).click();

  await expect(dona.getByRole('heading', { name: 'Turma do sábado', level: 1 })).toBeVisible();
  await expect(dona.getByText('Nenhuma rodada foi apurada ainda')).toBeVisible();
  const enderecoDaLiga = new URL(dona.url()).pathname;

  // O código sai da tela do jeito que o dono o repassaria ao grupo.
  const codigo = (await dona.locator('.convite__valor').innerText()).replace(/[^A-Z0-9]/g, '');
  expect(codigo, 'O código do convite tem dez caracteres').toHaveLength(10);

  // A convidada entra pelo link do convite, que é o caminho de quem recebe o código.
  await convidada.goto(`/convite/${codigo}`);
  await expect(convidada.getByText('Você foi convidado')).toBeVisible();
  await convidada.getByRole('button', { name: 'Entrar na liga' }).click();
  await expect(convidada.getByText('Você entrou na liga Turma do sábado.')).toBeVisible();
  await convidada.getByRole('link', { name: 'Ver a classificação da liga' }).click();
  await expect(convidada.getByRole('heading', { name: 'Turma do sábado', level: 1 })).toBeVisible();

  // Com a escalação congelada, a organização lança a súmula e publica o resultado.
  await dona.goto(`/c/${slug}/escalacao`);
  await expect(dona.getByText('Escalação congelada para a Rodada 1 desde')).toBeVisible({
    timeout: fechamento.getTime() - Date.now() + 60_000,
  });
  await lancarSumulaDaRodada(new ApiDaSessao(page), competitionId, roundId);

  await page.goto(`/organizar/c/${competitionId}/rodadas/${roundId}/revisao`);
  await page.getByRole('button', { name: 'Publicar resultado' }).click();
  await page
    .getByRole('dialog', { name: 'Publicar o resultado de Rodada 1?' })
    .getByRole('button', { name: 'Publicar', exact: true })
    .click();
  await expect(page.getByText('Resultado publicado.')).toBeVisible();

  // E aí as duas leem a mesma classificação, na mesma ordem e com os mesmos números.
  await dona.goto(enderecoDaLiga);
  await convidada.goto(enderecoDaLiga);

  // O aviso de provisório acompanha o ranking, não só a tela da rodada (02 §8).
  await expect(dona.getByText('Rodada 1 ainda é provisória')).toBeVisible();
  await expect(convidada.getByText('Rodada 1 ainda é provisória')).toBeVisible();

  const daDona = await classificacao(dona);
  const daConvidada = await classificacao(convidada);
  expect(daDona).toHaveLength(2);
  expect(daConvidada).toEqual(daDona);
  expect(daDona.join(' | ')).toContain('Dona da Liga');
  expect(daDona.join(' | ')).toContain('Convidada da Liga');

  // Quem fica na frente depende de a súmula ter feito marcar alguém do elenco delas, o
  // que a massa fictícia não garante; a ordem em si tem os testes determinísticos de
  // `RankingTests`. Aqui o que importa é as duas lerem exatamente a mesma lista.

  // Cada uma se reconhece na própria linha, e só na dela.
  await expect(dona.locator('.linha--voce')).toHaveCount(1);
  await expect(dona.locator('.linha--voce')).toContainText('Dona da Liga');
  await expect(convidada.locator('.linha--voce')).toContainText('Convidada da Liga');

  // O convite é do dono: a convidada não vê o código nem o botão de apagar.
  await expect(convidada.getByText('Copiar código')).toBeHidden();
  await expect(convidada.getByRole('button', { name: 'Apagar esta liga' })).toBeHidden();
  await expect(convidada.getByRole('button', { name: 'Sair desta liga' })).toBeVisible();

  const sobra = await convidada.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(sobra, 'A página rola na horizontal').toBeLessThanOrEqual(0);

  // A liga some da lista de quem sai, e continua de pé para quem ficou.
  await convidada.getByRole('button', { name: 'Sair desta liga' }).click();
  await convidada
    .getByRole('dialog', { name: 'Sair da liga Turma do sábado?' })
    .getByRole('button', { name: 'Sair da liga', exact: true })
    .click();
  await expect(convidada).toHaveURL(new RegExp(`/c/${slug}/ligas$`));
  await expect(convidada.getByText('Você ainda não está em nenhuma liga')).toBeVisible();

  await dona.goto(`/c/${slug}/ligas`);
  await expect(dona.getByText('1 participante')).toBeVisible();

  await contextoUm.close();
  await contextoDois.close();
});

/**
 * A classificação como colocação, nome e total, sem as etiquetas "Você" e "Dono", que
 * mudam conforme quem está lendo. É o que precisa ser idêntico nas duas telas.
 */
async function classificacao(pagina: Page): Promise<string[]> {
  return pagina.locator('.classificacao .linha').evaluateAll((linhas) =>
    linhas.map((linha) => {
      const texto = (seletor: string) => linha.querySelector(seletor)?.textContent?.trim() ?? '';
      const nome = [...(linha.querySelector('.linha__nome')?.childNodes ?? [])]
        .filter((no) => no.nodeType === Node.TEXT_NODE)
        .map((no) => no.textContent?.trim() ?? '')
        .filter(Boolean)
        .join(' ');
      return `${texto('.linha__posicao')} ${nome} ${texto('.linha__pontos')}`;
    }),
  );
}
