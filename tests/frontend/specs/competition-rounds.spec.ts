import { Page, expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

test('proprietário marca as partidas, abre o mercado e volta para rascunho', async ({
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

  await criarCampeonato(page, organizacao);
  const navegacao = page.getByRole('navigation', { name: 'Navegação do campeonato' });

  // Sem fase com times confirmados não há jogo possível, e a tela diz isso.
  await navegacao.getByRole('link', { name: 'Rodadas' }).click();
  await expect(page.getByRole('heading', { name: 'Rodadas', level: 1 })).toBeVisible();
  await expect(page.getByText('Crie uma fase e confirme os times dela')).toBeVisible();

  await montarFase(page, navegacao);

  await navegacao.getByRole('link', { name: 'Rodadas' }).click();
  await page.getByRole('button', { name: 'Adicionar rodada' }).click();
  await page.getByLabel('Nome da rodada').fill('Rodada 1');
  await page.getByRole('button', { name: 'Adicionar rodada' }).click();
  await expect(page.getByText('Rodada 1 criada.')).toBeVisible();

  const rodada = page.getByRole('region', { name: '1. Rodada 1' });
  await expect(rodada.getByText('Rascunho')).toBeVisible();
  await expect(rodada.getByText('Nenhuma partida marcada nesta rodada.')).toBeVisible();

  // A data vai no fuso do campeonato; o servidor é quem converte para UTC.
  const quando = proximoDomingo();
  await rodada.getByRole('button', { name: 'Adicionar partida em Rodada 1' }).click();
  await rodada.getByLabel('Mandante').selectOption({ label: 'União da Vila — Grupo A' });
  await rodada.getByLabel('Visitante').selectOption({ label: 'Estrela do Bairro — Grupo A' });
  await rodada.getByLabel('Data e hora').fill(quando.local);
  await rodada.getByRole('button', { name: 'Adicionar partida' }).click();
  await expect(page.getByText('Partida adicionada.')).toBeVisible();
  await expect(rodada.getByText('União da Vila × Estrela do Bairro')).toBeVisible();
  await expect(rodada.getByText(`${quando.exibido} · Fase de grupos · Grupo A`)).toBeVisible();

  // Abrir o mercado congela a lista e mostra o fechamento no fuso do campeonato.
  await rodada.getByRole('button', { name: 'Abrir mercado de Rodada 1' }).click();
  await expect(page.getByText('Mercado de Rodada 1 aberto.')).toBeVisible();
  await expect(rodada.getByText('Mercado aberto')).toBeVisible();
  await expect(rodada.getByText(/Mercado fecha em/)).toBeVisible();
  await expect(rodada.getByRole('button', { name: /^Adicionar partida/ })).toHaveCount(0);

  // Adiar continua valendo com o mercado aberto: é o jogo que não aconteceu.
  await rodada
    .getByRole('button', { name: 'Editar União da Vila contra Estrela do Bairro' })
    .click();
  await rodada.getByLabel('Situação').selectOption({ label: 'Adiada' });
  await rodada.getByRole('button', { name: 'Salvar partida' }).click();
  await expect(page.getByText('Partida salva.')).toBeVisible();
  await expect(rodada.getByText('Adiada')).toBeVisible();

  // Voltar para rascunho devolve a edição da lista de jogos.
  await rodada.getByRole('button', { name: 'Voltar para rascunho em Rodada 1' }).click();
  await expect(page.getByText('Rodada 1 voltou para rascunho.')).toBeVisible();
  await expect(rodada.getByRole('button', { name: /^Adicionar partida/ })).toBeVisible();

  // A ordem e a partida sobrevivem a recarregar a página.
  await page.reload();
  await expect(
    page
      .getByRole('region', { name: '1. Rodada 1' })
      .getByText('União da Vila × Estrela do Bairro'),
  ).toBeVisible();
});

async function criarCampeonato(page: Page, organizacao: string): Promise<void> {
  await page.goto('/organizar');
  await page
    .getByRole('region', { name: organizacao })
    .getByRole('link', { name: 'Campeonatos' })
    .click();
  await page.getByRole('link', { name: 'Criar campeonato' }).click();
  await page.getByLabel('Nome do campeonato').fill(`Copa das Rodadas ${Date.now()}`);
  await page.getByRole('radio', { name: /^Fut7/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato' }).click();
  await expect(page).toHaveURL(/\/organizar\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });
}

/** Dois times e uma fase de grupos com os dois confirmados no mesmo grupo. */
async function montarFase(page: Page, navegacao: ReturnType<Page['getByRole']>): Promise<void> {
  await navegacao.getByRole('link', { name: 'Times' }).click();
  for (const nome of ['União da Vila', 'Estrela do Bairro']) {
    await page.getByRole('button', { name: 'Adicionar time' }).click();
    await page.getByLabel('Nome do time').fill(nome);
    await page.getByRole('button', { name: 'Adicionar time' }).click();
    await expect(page.getByText(`${nome} adicionado.`)).toBeVisible();
  }

  await navegacao.getByRole('link', { name: 'Fases' }).click();
  await page.getByRole('button', { name: 'Adicionar fase' }).click();
  const nova = page.getByRole('region', { name: 'Nova fase' });
  await nova.getByLabel('Nome da fase').fill('Fase de grupos');
  await nova.getByRole('button', { name: 'Adicionar fase' }).click();
  await expect(page.getByText('Fase de grupos adicionada.')).toBeVisible();

  const fase = page.getByRole('region', { name: '1. Fase de grupos' });
  await fase.getByRole('button', { name: 'Gerenciar times de Fase de grupos' }).click();
  await fase.getByRole('checkbox', { name: 'União da Vila' }).check();
  await fase.getByRole('checkbox', { name: 'Estrela do Bairro' }).check();
  await fase.getByRole('button', { name: 'Salvar times' }).click();
  await expect(page.getByText('Times de Fase de grupos salvos.')).toBeVisible();
}

/**
 * Um domingo daqui a alguns dias, às 10h. Precisa estar no futuro: o servidor recusa
 * abrir um mercado que já teria fechado.
 */
function proximoDomingo(): { local: string; exibido: string } {
  const data = new Date();
  data.setDate(data.getDate() + 10);
  const ano = data.getFullYear();
  const mes = String(data.getMonth() + 1).padStart(2, '0');
  const dia = String(data.getDate()).padStart(2, '0');
  return { local: `${ano}-${mes}-${dia}T10:00`, exibido: `${dia}/${mes}/${ano} 10:00` };
}
