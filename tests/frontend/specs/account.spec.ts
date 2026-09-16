import { APIRequestContext, expect, test } from '@playwright/test';

/**
 * Fluxo de conta ponta a ponta, incluindo o e-mail de verdade.
 *
 * A mensagem é lida do Mailpit, o mesmo servidor SMTP que o compose sobe, então o
 * caminho testado é o real: API manda por SMTP, a pessoa abre o link, confirma e
 * entra. Um duplo de e-mail em memória não provaria isso.
 */
const MAILPIT = 'http://127.0.0.1:8025';

const SENHA = 'uma-senha-bem-longa-2026';

function emailUnico(): string {
  return `e2e-${Date.now()}-${Math.floor(Math.random() * 10_000)}@exemplo.local`;
}

/** Busca o corpo da última mensagem enviada para o endereço, esperando ela chegar. */
async function aguardarEmail(api: APIRequestContext, destinatario: string): Promise<string> {
  for (let tentativa = 0; tentativa < 30; tentativa++) {
    const lista = await api.get(`${MAILPIT}/api/v1/search?query=to:${destinatario}`);

    if (lista.ok()) {
      const corpo = (await lista.json()) as { messages?: { ID: string }[] };
      const primeira = corpo.messages?.[0];

      if (primeira) {
        const detalhe = await api.get(`${MAILPIT}/api/v1/message/${primeira.ID}`);
        const mensagem = (await detalhe.json()) as { Text?: string };
        if (mensagem.Text) {
          return mensagem.Text;
        }
      }
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error(`Nenhum e-mail chegou para ${destinatario} no Mailpit.`);
}

function extrairCaminho(corpo: string): string {
  const match = /http:\/\/localhost:4200(\/[^\s]+)/.exec(corpo);
  expect(match, `Nenhum link encontrado no e-mail:\n${corpo}`).not.toBeNull();
  return match![1];
}

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
  const verificacao = await aguardarEmail(request, email);
  await page.goto(extrairCaminho(verificacao));
  await expect(page.getByText('E-mail confirmado')).toBeVisible();

  // 3. Entrada
  await page.goto('/entrar');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();

  await expect(page).toHaveURL(/\/perfil$/);
  await expect(page.getByText(email)).toBeVisible();
  await expect(page.getByText('E-mail confirmado')).toBeVisible();

  // 4. Saída volta para a tela de entrada e a sessão some
  await page.getByRole('button', { name: 'Encerrar sessão' }).click();
  await expect(page).toHaveURL(/\/entrar/);

  await page.goto('/perfil');
  await expect(page).toHaveURL(/\/entrar\?destino=/);
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
