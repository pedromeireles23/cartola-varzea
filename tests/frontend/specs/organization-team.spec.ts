import { expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, aguardarEmail, emailUnico, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

test('proprietário convida e revoga auxiliar, e quem não é dono não vê a equipe', async ({
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

  // 1. A organização aprovada aparece em "Minhas organizações", com o papel.
  await page.goto('/perfil');
  await page.getByRole('link', { name: 'Minhas organizações' }).click();
  await expect(page).toHaveURL(/\/organizar$/);
  const cartao = page.getByRole('region', { name: organizacao });
  await expect(cartao.getByText('Proprietário')).toBeVisible();
  await cartao.getByRole('link', { name: 'Gerenciar equipe' }).click();
  await expect(page.getByRole('heading', { name: `Equipe de ${organizacao}` })).toBeVisible();
  const urlDaEquipe = page.url();

  // 2. Convite enviado aparece pendente e chega por e-mail com o link da área de organização.
  const convidado = emailUnico();
  await page.getByLabel('E-mail de quem vai auxiliar').fill(convidado);
  await page.getByRole('button', { name: 'Enviar convite' }).click();
  await expect(page.getByText(`Convite enviado para ${convidado}.`)).toBeVisible();

  const convite = page
    .getByRole('region', { name: 'Convites' })
    .getByRole('listitem')
    .filter({ hasText: convidado });
  await expect(convite.getByText('Pendente', { exact: true })).toBeVisible();

  const mensagem = await aguardarEmail(request, convidado, '/organizar/convite?token=');
  expect(mensagem).toContain(organizacao);

  // 3. Revogar pede confirmação e o convite deixa de estar pendente.
  await convite.getByRole('button', { name: 'Revogar' }).click();
  const dialogo = page.getByRole('dialog', { name: `Revogar convite de ${convidado}?` });
  await dialogo.getByRole('button', { name: 'Revogar convite' }).click();
  await expect(page.getByText(`Convite de ${convidado} revogado.`)).toBeVisible();
  await expect(convite.getByText('Revogado', { exact: true })).toBeVisible();
  await expect(convite.getByRole('button', { name: 'Revogar' })).toHaveCount(0);

  // 4. Nem a administração da plataforma vê a equipe de uma organização de que não é dona.
  await admin.goto(urlDaEquipe);
  await expect(
    admin.getByText('Só quem é proprietário da organização gerencia a equipe.'),
  ).toBeVisible();
  await expect(admin.getByLabel('E-mail de quem vai auxiliar')).toHaveCount(0);

  await contextoAdmin.close();
});
