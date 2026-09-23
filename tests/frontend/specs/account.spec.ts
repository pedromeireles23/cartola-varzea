import { expect, test } from '@playwright/test';

import { SENHA, aguardarEmail, emailUnico, extrairCaminho } from '../support/conta';

/**
 * Fluxo de conta ponta a ponta, incluindo o e-mail de verdade.
 *
 * A mensagem é lida do Mailpit, o mesmo servidor SMTP que o compose sobe, então o
 * caminho testado é o real: API manda por SMTP, a pessoa abre o link, confirma e
 * entra. Um duplo de e-mail em memória não provaria isso.
 */
test('cadastro, confirmação por e-mail, entrada e saída', async ({ page, request }) => {
  const email = emailUnico();

  // 1. Cadastro
  await page.goto('/cadastro');
  await page.getByLabel('Como quer ser chamado').fill('Pessoa E2E');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(SENHA);
  await page.getByRole('button', { name: 'Criar minha conta' }).click();

  // A confirmação é deliberadamente vaga: não diz se o e-mail já existia.
  await expect(page.getByText(/você vai receber uma mensagem/i)).toBeVisible();

  // 2. Confirmação pelo link que chegou no Mailpit
  const verificacao = await aguardarEmail(request, email, '/verificar-email');
  await page.goto(extrairCaminho(verificacao));
  await expect(page.getByText('E-mail confirmado')).toBeVisible();
  await expect(page).toHaveURL(/\/verificar-email$/);

  // 3. Entrada
  await page.goto('/entrar');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();

  // A entrada abre o início, que orienta a conta nova; os dados ficam em Conta.
  await expect(page).toHaveURL(/\/inicio$/);
  await expect(
    page.getByText('Sua conta está pronta. Agora escolha onde quer jogar.'),
  ).toBeVisible();
  await page.goto('/perfil');
  await expect(page.getByText(email)).toBeVisible();
  await expect(page.getByText('E-mail confirmado')).toBeVisible();

  // A sessão fica apenas no cookie HttpOnly. Nenhum token ou perfil é persistido
  // nos storages acessíveis por JavaScript.
  const storage = await page.evaluate(() => ({
    local: Object.keys(localStorage),
    session: Object.keys(sessionStorage),
  }));
  expect(storage).toEqual({ local: [], session: [] });

  const cookies = await page.context().cookies();
  const sessao = cookies.find((cookie) => cookie.name === 'fut7fantasy.session');
  expect(sessao?.httpOnly).toBe(true);
  expect(sessao?.sameSite).toBe('Lax');

  // 4. Saída volta para a tela de entrada e a sessão some
  await page.getByRole('button', { name: 'Encerrar sessão' }).click();
  await expect(page).toHaveURL(/\/entrar/);

  await page.goto('/perfil');
  await expect(page).toHaveURL(/\/entrar\?destino=/);

  // 5. Recuperação usa o link real do Mailpit e remove o token da URL depois
  // do uso. O mesmo fluxo confirma que a senha nova passa a valer.
  await page.goto('/recuperar-senha');
  await page.getByLabel('E-mail').fill(email);
  await page.getByRole('button', { name: 'Enviar link' }).click();
  await expect(page.getByText(/você vai receber uma mensagem/i)).toBeVisible();

  const recuperacao = await aguardarEmail(request, email, '/recuperar-senha');
  await page.goto(extrairCaminho(recuperacao));
  await page.getByRole('textbox', { name: /^Senha nova/ }).fill('outra-senha-bem-longa-2026');
  await page.getByRole('button', { name: 'Salvar senha nova' }).click();
  await expect(page.getByText('Senha redefinida')).toBeVisible();
  await expect(page).toHaveURL(/\/recuperar-senha$/);

  await page.goto('/entrar');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill('outra-senha-bem-longa-2026');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page).toHaveURL(/\/inicio$/);
});

test('entrada com senha errada não revela se a conta existe', async ({ page }) => {
  await page.goto('/entrar');
  await page.getByLabel('E-mail').fill(emailUnico());
  await page.getByLabel('Senha').fill('senha-errada-mas-longa');
  await page.getByRole('button', { name: 'Entrar' }).click();

  const alerta = page.getByRole('alert');
  await expect(alerta).toBeVisible();
  await expect(alerta).not.toContainText(/não existe|não encontrad|inexistente/i);
});

test('área privada redireciona para entrar preservando o destino', async ({ page }) => {
  await page.goto('/perfil');

  await expect(page).toHaveURL(/\/entrar\?destino=%2Fperfil/);
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible();
});
