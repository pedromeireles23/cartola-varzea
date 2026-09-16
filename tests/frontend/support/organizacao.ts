import { APIRequestContext, Page, expect } from '@playwright/test';

import { entrarComContaNova } from './conta';

/**
 * Cria uma conta nova com uma organização aprovada, pelo caminho real: a pessoa pede
 * acesso e a administração aprova na fila. `admin` precisa já ter entrado como
 * Platform admin.
 */
export async function criarOrganizacaoAprovada(
  dono: Page,
  admin: Page,
  api: APIRequestContext,
): Promise<{ email: string; organizacao: string }> {
  const email = await entrarComContaNova(dono, api, 'Pessoa Proprietária');
  const organizacao = `Liga Equipe ${Date.now()}-${Math.floor(Math.random() * 10_000)}`;

  await dono.goto('/organizar/solicitar');
  await dono.getByLabel('Nome da organização').fill(organizacao);
  await dono.getByRole('button', { name: 'Enviar solicitação' }).click();
  await expect(dono.getByRole('region', { name: 'Sua solicitação' })).toBeVisible();

  await admin.goto('/admin/solicitacoes');
  const cartao = admin.getByRole('region', { name: organizacao });
  await cartao.getByRole('button', { name: 'Aprovar', exact: true }).click();
  const dialogo = admin.getByRole('dialog', { name: `Aprovar ${organizacao}?` });
  await dialogo.getByLabel('Motivo').fill('Aprovada para o teste de equipe.');
  await dialogo.getByRole('button', { name: 'Confirmar aprovação' }).click();
  await expect(admin.getByText(`${organizacao} foi aprovada.`)).toBeVisible();

  return { email, organizacao };
}
