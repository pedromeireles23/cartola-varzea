import { expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

test('proprietário monta grupos e mata-mata, reordena e remove fases', async ({
  browser,
  page,
  request,
}) => {
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

  // Campeonato em rascunho, pelo caminho da interface.
  await page.goto('/organizar');
  await page
    .getByRole('region', { name: organizacao })
    .getByRole('link', { name: 'Campeonatos' })
    .click();
  await page.getByRole('link', { name: 'Criar campeonato' }).click();
  await page.getByLabel('Nome do campeonato').fill(`Copa das Fases ${Date.now()}`);
  await page.getByRole('radio', { name: /^Fut7/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato' }).click();
  await expect(page).toHaveURL(/\/organizar\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });

  // 1. Fase de grupos com três grupos e desempate ajustado.
  await page
    .getByRole('navigation', { name: 'Navegação do campeonato' })
    .getByRole('link', { name: 'Fases' })
    .click();
  await expect(page.getByRole('heading', { name: 'Fases', level: 1 })).toBeVisible();
  await expect(page.getByText('Nenhuma fase ainda.')).toBeVisible();
  await page.getByRole('button', { name: 'Adicionar fase' }).click();

  const nova = page.getByRole('region', { name: 'Nova fase' });
  await nova.getByLabel('Nome da fase').fill('Fase de grupos');
  await nova.getByLabel('Quantidade de grupos').selectOption('3');
  await expect(nova.getByLabel('Nome do grupo 3')).toHaveValue('Grupo C');
  await nova.getByRole('checkbox', { name: 'Menos cartões amarelos' }).uncheck();
  await nova.getByRole('button', { name: 'Subir Confronto direto' }).click();
  await nova.getByRole('button', { name: 'Adicionar fase' }).click();
  await expect(page.getByText('Fase de grupos adicionada.')).toBeVisible();

  const grupos = page.getByRole('region', { name: '1. Fase de grupos' });
  await expect(grupos.getByText('Grupo A, Grupo B, Grupo C')).toBeVisible();
  await expect(grupos.getByRole('listitem')).toHaveText([
    'Mais vitórias',
    'Maior saldo de gols',
    'Confronto direto',
    'Mais gols marcados',
    'Menos cartões vermelhos',
  ]);

  // 2. Mata-mata entra depois e pode subir para antes dos grupos.
  await page.getByRole('button', { name: 'Adicionar fase' }).click();
  await nova.getByLabel('Nome da fase').fill('Mata-mata');
  await nova.getByRole('radio', { name: /^Mata-mata/ }).check();
  await expect(nova.getByLabel('Quantidade de grupos')).toHaveCount(0);
  await nova.getByRole('button', { name: 'Adicionar fase' }).click();
  await expect(page.getByRole('region', { name: '2. Mata-mata' })).toBeVisible();

  await page.getByRole('button', { name: 'Subir Mata-mata' }).click();
  await expect(page.getByText('Mata-mata agora é a fase 1.')).toBeVisible();
  await expect(page.getByRole('region', { name: '1. Mata-mata' })).toBeVisible();
  await expect(page.getByRole('region', { name: '2. Fase de grupos' })).toBeVisible();

  // 3. Editar mantém a fase e renomeia um grupo.
  const segunda = page.getByRole('region', { name: '2. Fase de grupos' });
  await segunda.getByRole('button', { name: 'Editar Fase de grupos' }).click();
  await segunda.getByLabel('Nome do grupo 3').fill('Grupo da Morte');
  await segunda.getByRole('button', { name: 'Salvar fase' }).click();
  await expect(page.getByText('Fase de grupos salva.')).toBeVisible();
  await expect(segunda.getByText('Grupo A, Grupo B, Grupo da Morte')).toBeVisible();

  // 4. Remover pede confirmação e renumera a fase seguinte.
  await page.getByRole('button', { name: 'Remover Mata-mata' }).click();
  const dialogo = page.getByRole('dialog', { name: 'Remover Mata-mata?' });
  await dialogo.getByRole('button', { name: 'Remover fase' }).click();
  await expect(page.getByText('Mata-mata removida.')).toBeVisible();
  await expect(page.getByRole('region', { name: '1. Fase de grupos' })).toBeVisible();
  await expect(page.getByRole('region', { name: /Mata-mata/ })).toHaveCount(0);

  // 5. A ordem sobrevive a recarregar a página.
  await page.reload();
  await expect(page.getByRole('region', { name: '1. Fase de grupos' })).toBeVisible();
});
