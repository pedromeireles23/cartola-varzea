import { expect, test } from '@playwright/test';

import {
  ADMIN_E2E_EMAIL,
  SENHA,
  aguardarEmail,
  entrarComContaNova,
  entrarComoAdmin,
  extrairCaminho,
} from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

test('convidado aceita pelo link do e-mail, a conta errada não consome o convite e a remoção corta o acesso', async ({
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

  // O dono usa `page`; quem vai auxiliar tem o próprio navegador, sem sessão no início.
  const { organizacao } = await criarOrganizacaoAprovada(page, admin, request);
  const contextoConvidado = await browser.newContext();
  const convidado = await contextoConvidado.newPage();
  const emailConvidado = await entrarComContaNova(convidado, request, 'Pessoa Auxiliar');
  await convidado.getByRole('button', { name: 'Encerrar sessão' }).click();
  await expect(convidado).toHaveURL(/\/entrar/);

  // 1. Dono convida.
  await page.goto('/organizar');
  await page
    .getByRole('region', { name: organizacao })
    .getByRole('link', { name: 'Gerenciar equipe' })
    .click();
  await expect(page).toHaveURL(/\/organizar\/o\/[^/]+\/equipe$/);
  const urlDaEquipe = page.url();
  await page.getByLabel('E-mail de quem vai auxiliar').fill(emailConvidado);
  await page.getByRole('button', { name: 'Enviar convite' }).click();
  await expect(page.getByText(`Convite enviado para ${emailConvidado}.`)).toBeVisible();
  const linkDoConvite = extrairCaminho(
    await aguardarEmail(request, emailConvidado, '/organizar/convite?token='),
  );

  // 2. A conta errada (o próprio dono) abre o link: o token sai da URL e o aceite é recusado
  //    sem consumir o convite.
  await page.goto(linkDoConvite);
  await expect(page).toHaveURL(/\/organizar\/convite$/);
  await page.getByRole('button', { name: 'Aceitar convite' }).click();
  await expect(page.getByText('Este convite foi enviado para outro e-mail.')).toBeVisible();

  // 3. Quem foi convidado abre o link sem sessão, entra e volta para aceitar.
  await convidado.goto(linkDoConvite);
  await expect(convidado).toHaveURL(/\/organizar\/convite$/);
  await convidado
    .getByRole('region', { name: 'Entre para aceitar' })
    .getByRole('link', { name: 'Entrar', exact: true })
    .click();
  await expect(convidado).toHaveURL(/\/entrar\?destino=%2Forganizar%2Fconvite$/);
  await convidado.getByLabel('E-mail').fill(emailConvidado);
  await convidado.getByLabel('Senha').fill(SENHA);
  await convidado.getByRole('button', { name: 'Entrar' }).click();
  await expect(convidado).toHaveURL(/\/organizar\/convite$/);

  await convidado.getByRole('button', { name: 'Aceitar convite' }).click();
  await expect(convidado.getByText('Convite aceito.')).toBeVisible();
  const tokens = await convidado.evaluate(() => ({
    local: Object.keys(localStorage),
    session: Object.keys(sessionStorage),
  }));
  expect(tokens).toEqual({ local: [], session: [] });

  // 4. A organização aparece para quem auxilia, sem gestão de equipe.
  await convidado.getByRole('link', { name: 'Ver minhas organizações' }).click();
  const cartao = convidado.getByRole('region', { name: organizacao });
  await expect(cartao.getByText('Auxiliar', { exact: true })).toBeVisible();
  await expect(cartao.getByRole('link', { name: 'Gerenciar equipe' })).toHaveCount(0);

  // 5. O dono vê a pessoa na equipe e o convite aceito.
  await page.goto(urlDaEquipe);
  await expect(
    page.getByRole('region', { name: 'Auxiliares' }).getByText('Pessoa Auxiliar'),
  ).toBeVisible();
  await expect(
    page
      .getByRole('region', { name: 'Convites' })
      .getByRole('listitem')
      .filter({ hasText: emailConvidado })
      .getByText('Aceito', { exact: true }),
  ).toBeVisible();

  // 6. O dono remove a pessoa; na sessão que ela já tinha aberta, a organização some.
  const auxiliares = page.getByRole('region', { name: 'Auxiliares' });
  await auxiliares
    .getByRole('listitem')
    .filter({ hasText: 'Pessoa Auxiliar' })
    .getByRole('button', { name: 'Remover', exact: true })
    .click();
  await page
    .getByRole('dialog', { name: 'Remover Pessoa Auxiliar da equipe?' })
    .getByRole('button', { name: 'Remover da equipe' })
    .click();
  await expect(page.getByText('Pessoa Auxiliar saiu da equipe.')).toBeVisible();
  await expect(auxiliares.getByText('Ninguém auxilia esta organização ainda.')).toBeVisible();

  await convidado.reload();
  await expect(convidado.getByText('Nenhuma organização ainda')).toBeVisible();
  await expect(convidado.getByRole('region', { name: organizacao })).toHaveCount(0);

  await contextoConvidado.close();
  await contextoAdmin.close();
});
