import { expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

test('proprietário inscreve, precifica e desliga atleta sem apagar o histórico', async ({
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

  await page.goto('/organizar');
  await page
    .getByRole('region', { name: organizacao })
    .getByRole('link', { name: 'Campeonatos' })
    .click();
  await page.getByRole('link', { name: 'Criar campeonato' }).click();
  await page.getByLabel('Nome do campeonato').fill(`Copa dos Atletas ${Date.now()}`);
  await page.getByRole('radio', { name: /^Fut7/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato' }).click();
  await expect(page).toHaveURL(/\/organizar\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });

  const navigation = page.getByRole('navigation', { name: 'Navegação do campeonato' });
  await navigation.getByRole('link', { name: 'Times' }).click();
  await page.getByRole('button', { name: 'Adicionar time' }).click();
  await page.getByLabel('Nome do time').fill('União da Vila');
  await page.getByRole('button', { name: 'Adicionar time' }).click();
  await expect(page.getByText('União da Vila adicionado.')).toBeVisible();

  await navigation.getByRole('link', { name: 'Atletas' }).click();
  await expect(page.getByRole('heading', { name: 'Atletas', level: 1 })).toBeVisible();
  await expect(page.getByText('Nenhum atleta cadastrado.')).toBeVisible();

  await page.getByRole('button', { name: 'Adicionar atleta' }).click();
  await page.getByLabel('Nome esportivo').fill('Bia');
  await page.getByLabel('Posição').selectOption('Midfielder');
  await page.getByLabel('Nível de preço').selectOption('Star');
  await page.getByLabel('Preço exato (opcional)').fill('10.25');
  await page.getByRole('button', { name: 'Adicionar atleta' }).click();
  await expect(page.getByText('Bia adicionado.')).toBeVisible();

  const athlete = page.getByRole('listitem').filter({ hasText: 'Bia' });
  await expect(athlete).toContainText('União da Vila');
  await expect(athlete).toContainText('Meio-campista');
  await expect(athlete).toContainText('Destaque');
  await expect(athlete).toContainText('10,25 créditos');
  await expect(athlete).toContainText('Disponível');

  await athlete.getByRole('button', { name: 'Desligar Bia' }).click();
  const dialog = page.getByRole('dialog', { name: 'Desligar Bia?' });
  await dialog.getByRole('button', { name: 'Desligar atleta' }).click();
  await expect(page.getByText('Bia desligado.')).toBeVisible();
  await expect(athlete).toContainText('Desligado');
  await expect(athlete.getByRole('button', { name: 'Editar Bia' })).toHaveCount(0);

  await page.reload();
  await expect(page.getByRole('listitem').filter({ hasText: 'Bia' })).toContainText('Desligado');
});
