import { expect, test as setup } from '@playwright/test';

import {
  ADMIN_E2E_EMAIL,
  SENHA,
  aguardarEmail,
  digitarCodigo,
  enviarSenha,
  extrairCaminho,
  guardarChaveDoAdmin,
  lerChaveDoAdmin,
} from '../support/conta';
import { codigoTotp } from '../support/totp';

/**
 * Cria a conta de administração do E2E se ela ainda não existir, e liga os dois fatores
 * que a administração exige (Fase 20).
 *
 * O papel vem da configuração da API na confirmação do e-mail, e os dois fatores pela tela
 * de Conta, pelo mesmo caminho de produção; nada é gravado direto no banco. Numa base
 * reaproveitada a conta já existe, e o setup só confirma que ela entra com o código.
 */
setup('conta de administração do E2E existe, com os dois fatores', async ({ page, request }) => {
  let entrada = await enviarSenha(page, ADMIN_E2E_EMAIL);

  if (entrada === 'codigo') {
    await digitarCodigo(page, await lerChaveDoAdmin());
    return;
  }

  if (entrada === 'recusada') {
    await page.goto('/cadastro');
    await page.getByLabel('Como quer ser chamado').fill('Administração E2E');
    await page.getByLabel('E-mail').fill(ADMIN_E2E_EMAIL);
    await page.getByLabel('Senha').fill(SENHA);
    await page.getByRole('button', { name: 'Criar minha conta' }).click();
    await expect(page.getByText(/você vai receber uma mensagem/i)).toBeVisible();

    const verificacao = await aguardarEmail(request, ADMIN_E2E_EMAIL, '/verificar-email');
    await page.goto(extrairCaminho(verificacao));
    await expect(page.getByText('E-mail confirmado')).toBeVisible();

    entrada = await enviarSenha(page, ADMIN_E2E_EMAIL);
  }

  expect(entrada).toBe('entrou');

  // Sem o papel, a API em execução não tem esta conta como administrador inicial; os testes
  // de administração avisam isso e param ali.
  const conta = (await (await page.request.get('/api/v1/auth/me')).json()) as { roles: string[] };
  if (!conta.roles.includes('PlatformAdmin')) {
    return;
  }

  await page.goto('/perfil');
  await page.getByRole('button', { name: 'Ligar a verificação em duas etapas' }).click();
  const chave = (await page.locator('code.chave').textContent())!.replace(/\s/g, '');
  await page.getByLabel('Código do aplicativo').fill(codigoTotp(chave));
  await page.getByRole('button', { name: 'Confirmar e ligar' }).click();
  await expect(page.getByText('Verificação em duas etapas ligada.')).toBeVisible();
  await guardarChaveDoAdmin(chave);
});
