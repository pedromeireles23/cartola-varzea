import { expect, test as setup } from '@playwright/test';

import {
  ADMIN_E2E_EMAIL,
  SENHA,
  aguardarEmail,
  extrairCaminho,
  tentarEntrar,
} from '../support/conta';

/**
 * Cria a conta de administração do E2E se ela ainda não existir.
 *
 * O papel vem da configuração da API na confirmação do e-mail, pelo mesmo caminho
 * de produção; nada é gravado direto no banco. Numa base reaproveitada a conta já
 * existe e o setup só confirma que ela entra.
 */
setup('conta de administração do E2E existe', async ({ page, request }) => {
  if (await tentarEntrar(page, ADMIN_E2E_EMAIL)) {
    return;
  }

  await page.goto('/cadastro');
  await page.getByLabel('Como quer ser chamado').fill('Administração E2E');
  await page.getByLabel('E-mail').fill(ADMIN_E2E_EMAIL);
  await page.getByLabel('Senha').fill(SENHA);
  await page.getByRole('button', { name: 'Criar minha conta' }).click();
  await expect(page.getByText(/você vai receber uma mensagem/i)).toBeVisible();

  const verificacao = await aguardarEmail(request, ADMIN_E2E_EMAIL, '/verificar-email');
  await page.goto(extrairCaminho(verificacao));
  await expect(page.getByText('E-mail confirmado')).toBeVisible();

  expect(await tentarEntrar(page, ADMIN_E2E_EMAIL)).toBe(true);
});
