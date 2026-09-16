import { expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

test('proprietário cadastra, edita e arquiva times sem perder o histórico', async ({
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
  await page.getByLabel('Nome do campeonato').fill(`Copa dos Times ${Date.now()}`);
  await page.getByRole('radio', { name: /^Fut7/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato' }).click();
  await expect(page).toHaveURL(/\/organizar\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });

  await page
    .getByRole('navigation', { name: 'Navegação do campeonato' })
    .getByRole('link', { name: 'Times' })
    .click();
  await expect(page.getByRole('heading', { name: 'Times', level: 1 })).toBeVisible();
  await expect(page.getByText('Nenhum time cadastrado.')).toBeVisible();

  await page.getByRole('button', { name: 'Adicionar time' }).click();
  await page.getByLabel('Nome do time').fill('União da Vila');
  await page.getByRole('button', { name: 'Adicionar time' }).click();
  await expect(page.getByText('União da Vila adicionado.')).toBeVisible();

  const time = page.getByRole('listitem').filter({ hasText: 'União da Vila' });
  await expect(time.getByText('UV', { exact: true })).toBeVisible();
  await time.getByRole('button', { name: 'Editar União da Vila' }).click();
  await time.getByLabel('Nome de União da Vila').fill('União FC');
  await time.getByRole('button', { name: 'Salvar' }).click();
  await expect(page.getByText('União FC salvo.')).toBeVisible();

  const renomeado = page.getByRole('listitem').filter({ hasText: 'União FC' });
  await renomeado.getByRole('button', { name: 'Arquivar União FC' }).click();
  const dialogo = page.getByRole('dialog', { name: 'Arquivar União FC?' });
  await dialogo.getByRole('button', { name: 'Arquivar time' }).click();
  await expect(page.getByText('União FC arquivado.')).toBeVisible();
  await expect(renomeado.getByText('Arquivado')).toBeVisible();
  await expect(renomeado.getByRole('button', { name: 'Editar União FC' })).toHaveCount(0);

  await page.reload();
  await expect(page.getByRole('listitem').filter({ hasText: 'União FC' })).toContainText(
    'Arquivado',
  );
});
