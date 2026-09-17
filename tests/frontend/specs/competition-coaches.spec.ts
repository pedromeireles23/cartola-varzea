import { expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

test('time recebe técnico automático e proprietário pode informar ou remover o nome pessoal', async ({
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
  await page.getByLabel('Nome do campeonato').fill(`Copa dos Técnicos ${Date.now()}`);
  await page.getByRole('radio', { name: /^Fut7/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato' }).click();
  await expect(page).toHaveURL(/\/organizar\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });

  const navigation = page.getByRole('navigation', { name: 'Navegação do campeonato' });
  await navigation.getByRole('link', { name: 'Times' }).click();
  await page.getByRole('button', { name: 'Adicionar time' }).click();
  await page.getByLabel('Nome do time').fill('União da Vila');
  await page.getByRole('button', { name: 'Adicionar time' }).click();
  await expect(page.getByText('União da Vila adicionado.')).toBeVisible();

  await navigation.getByRole('link', { name: 'Técnicos' }).click();
  await expect(page.getByRole('heading', { name: 'Técnicos', level: 1 })).toBeVisible();
  const coach = page.locator('.coaches').getByRole('listitem');
  await expect(coach).toContainText('Técnico do União da Vila');
  await expect(coach).toContainText('Nome automático');
  await expect(coach).toContainText('8,00 créditos');

  await coach.getByRole('button', { name: 'Editar Técnico do União da Vila' }).click();
  await page.getByLabel('Nome da pessoa (opcional)').fill('Ana Lima');
  await page.getByLabel('Nível de preço').selectOption('Star');
  await page.getByLabel('Preço exato (opcional)').fill('10.25');
  await coach.getByRole('button', { name: 'Salvar' }).click();
  await expect(page.getByText('Ana Lima salvo.')).toBeVisible();
  await expect(coach).toContainText('Destaque');
  await expect(coach).toContainText('10,25 créditos');

  await coach.getByRole('button', { name: 'Editar Ana Lima' }).click();
  await page.getByLabel('Nome da pessoa (opcional)').fill('');
  await coach.getByRole('button', { name: 'Salvar' }).click();
  await expect(page.getByText('Técnico do União da Vila salvo.')).toBeVisible();

  await page.reload();
  await expect(page.locator('.coaches').getByRole('listitem')).toContainText(
    'Técnico do União da Vila',
  );
});
