import { Page, expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

/** Elenco mínimo do Fut7: titulares da formação e um reserva por posição. */
const ELENCO: readonly (readonly [string, number])[] = [
  ['Goalkeeper', 2],
  ['Defender', 3],
  ['Midfielder', 3],
  ['Forward', 3],
];

test('visitante sem conta encontra o campeonato publicado e não o rascunho', async ({
  browser,
  page,
  request,
}) => {
  test.slow();
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

  // Nome único por execução: a busca pública enxerga o banco inteiro.
  const marca = `Varzea${Date.now()}`;
  const publicado = `Copa ${marca}`;
  const rascunho = `Rascunho ${marca}`;

  await criarCampeonato(page, organizacao, rascunho);
  await criarCampeonato(page, organizacao, publicado);
  const navegacao = page.getByRole('navigation', { name: 'Navegação do campeonato' });
  await abastecer(page, navegacao);

  await navegacao.getByRole('link', { name: 'Publicação' }).click();
  await page.getByRole('button', { name: 'Publicar campeonato' }).click();
  await page
    .getByRole('dialog', { name: 'Publicar o campeonato?' })
    .getByRole('button', { name: 'Publicar', exact: true })
    .click();
  await expect(page.getByText('Campeonato publicado.')).toBeVisible();

  const endereco = `/c/copa-${marca.toLowerCase()}-2026`;
  await expect(page.getByRole('link', { name: endereco })).toBeVisible();

  // A partir daqui é um visitante: contexto novo, sem cookie de sessão.
  const anonimo = await browser.newContext();
  const visitante = await anonimo.newPage();

  await visitante.goto('/campeonatos');
  await expect(visitante.getByRole('heading', { name: 'Campeonatos', level: 1 })).toBeVisible();
  await visitante.getByLabel('Buscar campeonato').fill(marca);
  await visitante.getByRole('button', { name: 'Buscar' }).click();

  await expect(visitante.getByRole('link', { name: publicado })).toBeVisible();
  await expect(visitante.getByRole('link', { name: rascunho })).toHaveCount(0);
  await expect(visitante).toHaveURL(new RegExp(`busca=${marca}`));

  await visitante.getByRole('link', { name: publicado }).click();
  await expect(visitante).toHaveURL(new RegExp(`${endereco}$`));
  await expect(visitante.getByRole('heading', { name: publicado, level: 1 })).toBeVisible();
  await expect(visitante.getByText(organizacao)).toBeVisible();
  await expect(visitante.getByText('1. Fase única')).toBeVisible();
  // Os times da fase saem em ordem alfabética, não na ordem em que foram cadastrados.
  await expect(visitante.getByText('Estrela do Bairro, União da Vila')).toBeVisible();

  // O visitante vê o campeonato, nunca a área de organização dele.
  await expect(visitante.getByRole('link', { name: 'Configuração' })).toHaveCount(0);

  // O ranking é público e existe antes da primeira rodada, dizendo que ainda não se mexeu.
  await visitante.getByRole('link', { name: 'Ver o ranking' }).click();
  await expect(visitante).toHaveURL(new RegExp(`${endereco}/ranking$`));
  await expect(
    visitante.getByRole('heading', { name: `Ranking de ${publicado}`, level: 1 }),
  ).toBeVisible();
  await expect(visitante.getByText('Nenhuma rodada foi apurada ainda')).toBeVisible();
  await expect(visitante.getByText('Ninguém entrou neste campeonato ainda')).toBeVisible();
  await visitante.getByRole('link', { name: 'Voltar ao campeonato' }).click();

  // Recarregar prova que o endereço vale sozinho, sem depender da navegação.
  await visitante.reload();
  await expect(visitante.getByRole('heading', { name: publicado, level: 1 })).toBeVisible();

  // Endereço inexistente é um estado explicado, não um erro.
  await visitante.goto('/c/campeonato-que-nao-existe-2026');
  await expect(visitante.getByText('Não encontramos este campeonato.')).toBeVisible();

  // Voltar para rascunho tira o campeonato do ar para quem está de fora.
  await page.getByRole('button', { name: 'Voltar para rascunho' }).click();
  await page
    .getByRole('dialog', { name: 'Voltar para rascunho?' })
    .getByRole('button', { name: 'Voltar para rascunho' })
    .click();
  await expect(page.getByText('Campeonato de volta para rascunho.')).toBeVisible();

  await visitante.goto(endereco);
  await expect(visitante.getByText('Não encontramos este campeonato.')).toBeVisible();
  await anonimo.close();
});

async function criarCampeonato(page: Page, organizacao: string, nome: string): Promise<void> {
  await page.goto('/organizar');
  await page
    .getByRole('region', { name: organizacao })
    .getByRole('link', { name: 'Campeonatos' })
    .click();
  await page.getByRole('link', { name: 'Criar campeonato' }).click();
  await page.getByLabel('Nome do campeonato').fill(nome);
  await page.getByRole('radio', { name: /^Fut7/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato' }).click();
  await expect(page).toHaveURL(/\/organizar\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });
}

/** Times, elenco mínimo e uma fase com participantes: o que o checklist exige. */
async function abastecer(page: Page, navegacao: ReturnType<Page['getByRole']>): Promise<void> {
  await navegacao.getByRole('link', { name: 'Times' }).click();
  for (const nome of ['União da Vila', 'Estrela do Bairro']) {
    await page.getByRole('button', { name: 'Adicionar time' }).click();
    await page.getByLabel('Nome do time').fill(nome);
    await page.getByRole('button', { name: 'Adicionar time' }).click();
    await expect(page.getByText(`${nome} adicionado.`)).toBeVisible();
  }

  await navegacao.getByRole('link', { name: 'Atletas' }).click();
  let numero = 0;
  for (const [posicao, quantidade] of ELENCO) {
    for (let indice = 0; indice < quantidade; indice++) {
      numero++;
      const nome = `Atleta ${numero}`;
      await page.getByRole('button', { name: 'Adicionar atleta' }).click();
      await page.getByLabel('Nome esportivo').fill(nome);
      await page
        .getByLabel('Time')
        .selectOption({ label: numero % 2 === 1 ? 'União da Vila' : 'Estrela do Bairro' });
      await page.getByLabel('Posição').selectOption(posicao);
      await page.getByLabel('Nível de preço').selectOption(indice === 0 ? 'Star' : 'Regular');
      await page.getByRole('button', { name: 'Adicionar atleta' }).click();
      await expect(page.getByText(`${nome} adicionado.`)).toBeVisible();
    }
  }

  await navegacao.getByRole('link', { name: 'Fases' }).click();
  await page.getByRole('button', { name: 'Adicionar fase' }).click();
  const nova = page.getByRole('region', { name: 'Nova fase' });
  await nova.getByLabel('Nome da fase').fill('Fase única');
  await nova.getByRole('radio', { name: /^Mata-mata/ }).check();
  await nova.getByRole('button', { name: 'Adicionar fase' }).click();
  await expect(page.getByText('Fase única adicionada.')).toBeVisible();

  const fase = page.getByRole('region', { name: '1. Fase única' });
  await fase.getByRole('button', { name: 'Gerenciar times de Fase única' }).click();
  await fase.getByRole('checkbox', { name: 'União da Vila' }).check();
  await fase.getByRole('checkbox', { name: 'Estrela do Bairro' }).check();
  await fase.getByRole('button', { name: 'Salvar times' }).click();
  await expect(page.getByText('Times de Fase única salvos.')).toBeVisible();
}
