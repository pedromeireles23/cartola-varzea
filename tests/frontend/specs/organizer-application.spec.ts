import { expect, test } from '@playwright/test';

import { entrarComContaNova } from '../support/conta';

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
