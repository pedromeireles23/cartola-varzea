import { expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComContaNova, entrarComoAdmin } from '../support/conta';

test('pessoa com conta solicita acesso de organizador e acompanha o pedido', async ({
  page,
  request,
}) => {
  await entrarComContaNova(page, request);
  const organizacao = `Liga E2E ${Date.now()}`;

  // 1. O caminho parte do perfil, como no mapa de navegação.
  await page.getByRole('link', { name: 'Quero organizar' }).click();
  await expect(page).toHaveURL(/\/organizar\/solicitar$/);
  await expect(page.getByRole('heading', { name: 'Organizar campeonatos' })).toBeVisible();

  // 2. Nome curto é recusado na própria tela, com o erro ligado ao campo.
  const campo = page.getByLabel('Nome da organização');
  await campo.fill('AB');
  await page.getByRole('button', { name: 'Enviar solicitação' }).click();
  await expect(campo).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByText(/entre 3 e 120 caracteres/)).toBeVisible();

  // 3. Envio válido troca o formulário pelo acompanhamento e leva o foco até ele.
  await campo.fill(organizacao);
  await page.getByRole('button', { name: 'Enviar solicitação' }).click();

  const acompanhamento = page.getByRole('region', { name: 'Sua solicitação' });
  await expect(acompanhamento).toBeVisible();
  await expect(acompanhamento.getByText('Solicitação enviada')).toBeVisible();
  await expect(acompanhamento.getByText(organizacao)).toBeVisible();
  await expect(acompanhamento.getByText('Em análise', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Enviar solicitação' })).toHaveCount(0);
  await expect(page.locator('div[tabindex="-1"]', { has: acompanhamento })).toBeFocused();

  // 4. O pedido foi persistido: ao voltar, continua em análise e sem novo envio.
  await page.reload();
  await expect(acompanhamento.getByText(organizacao)).toBeVisible();
  await expect(acompanhamento.getByText('Em análise', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Enviar solicitação' })).toHaveCount(0);
});

test('solicitar acesso exige login e preserva o destino', async ({ page }) => {
  await page.goto('/organizar/solicitar');

  await expect(page).toHaveURL(/\/entrar\?destino=%2Forganizar%2Fsolicitar/);
});

test('admin recusa com motivo, o pedido é reenviado e a aprovação cria a organização', async ({
  browser,
  page,
  request,
}) => {
  const contextoAdmin = await browser.newContext();
  const admin = await contextoAdmin.newPage();
  const ehAdmin = await entrarComoAdmin(admin);
  test.skip(
    !ehAdmin && !process.env['CI'],
    `A API local em execução não tem ${ADMIN_E2E_EMAIL} como administrador inicial. ` +
      'Pare a API para o Playwright subir a dele, ou configure PlatformAdministration:InitialAdminEmail.',
  );
  expect(ehAdmin, 'A conta do E2E deveria ser Platform admin').toBe(true);

  // 1. Pessoa pede acesso.
  const email = await entrarComContaNova(page, request, 'Pessoa Solicitante');
  const primeiroNome = `Liga Recusada ${Date.now()}`;
  await page.goto('/organizar/solicitar');
  await page.getByLabel('Nome da organização').fill(primeiroNome);
  await page.getByRole('button', { name: 'Enviar solicitação' }).click();
  await expect(page.getByRole('region', { name: 'Sua solicitação' })).toBeVisible();

  // 2. Admin encontra o pedido pelo perfil e recusa com motivo.
  await admin.goto('/perfil');
  await admin.getByRole('link', { name: 'Ver solicitações' }).click();
  await expect(admin).toHaveURL(/\/admin\/solicitacoes$/);

  const cartaoRecusado = admin.getByRole('region', { name: primeiroNome });
  await expect(cartaoRecusado.getByText(email)).toBeVisible();
  await cartaoRecusado.getByRole('button', { name: 'Não aprovar' }).click();

  const dialogoRecusa = admin.getByRole('dialog', { name: `Não aprovar ${primeiroNome}?` });
  await expect(dialogoRecusa.getByText('Quem pediu vai ler este motivo')).toBeVisible();
  await dialogoRecusa.getByLabel('Motivo').fill('Informe o nome oficial da liga.');
  await dialogoRecusa.getByRole('button', { name: 'Confirmar recusa' }).click();

  await expect(admin.getByText(`A solicitação de ${primeiroNome} não foi aprovada.`)).toBeVisible();
  await expect(cartaoRecusado).toHaveCount(0);

  // 3. Quem pediu vê o motivo e reenvia.
  await page.reload();
  await expect(page.getByText('Informe o nome oficial da liga.')).toBeVisible();
  const segundoNome = `Liga Aprovada ${Date.now()}`;
  await page.getByLabel('Nome da organização').fill(segundoNome);
  await page.getByRole('button', { name: 'Enviar solicitação' }).click();
  await expect(page.getByRole('region', { name: 'Sua solicitação' })).toBeVisible();

  // 4. Admin aprova; o motivo fica só na auditoria.
  await admin.reload();
  const cartaoAprovado = admin.getByRole('region', { name: segundoNome });
  await cartaoAprovado.getByRole('button', { name: 'Aprovar', exact: true }).click();
  const dialogoAprovacao = admin.getByRole('dialog', { name: `Aprovar ${segundoNome}?` });
  await dialogoAprovacao.getByLabel('Motivo').fill('Responsável conhecido na região.');
  await dialogoAprovacao.getByRole('button', { name: 'Confirmar aprovação' }).click();
  await expect(admin.getByText(`${segundoNome} foi aprovada.`)).toBeVisible();

  // 5. Quem pediu vê a aprovação, sem o motivo interno, e o histórico das duas.
  await page.reload();
  await expect(page.getByText(`A organização ${segundoNome} foi aprovada.`)).toBeVisible();
  await expect(page.getByText('Responsável conhecido na região.')).toHaveCount(0);
  const historico = page.getByRole('region', { name: 'Histórico' });
  await expect(historico.getByText('Não aprovada', { exact: true })).toBeVisible();
  await expect(historico.getByText('Aprovada', { exact: true })).toBeVisible();

  await contextoAdmin.close();
});
