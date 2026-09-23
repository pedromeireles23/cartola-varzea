import { APIRequestContext, Page, expect } from '@playwright/test';

/**
 * Apoio compartilhado dos E2E de conta.
 *
 * O e-mail é lido do Mailpit, o mesmo servidor SMTP que o compose sobe, então o
 * caminho testado é o real: a API manda por SMTP e a pessoa segue o link.
 */
export const MAILPIT = 'http://127.0.0.1:8025';

export const SENHA = 'uma-senha-bem-longa-2026';

export function emailUnico(): string {
  return `e2e-${Date.now()}-${Math.floor(Math.random() * 10_000)}@exemplo.local`;
}

/** Busca o corpo da última mensagem enviada para o endereço, esperando ela chegar. */
export async function aguardarEmail(
  api: APIRequestContext,
  destinatario: string,
  caminhoEsperado: string,
): Promise<string> {
  for (let tentativa = 0; tentativa < 30; tentativa++) {
    const lista = await api.get(`${MAILPIT}/api/v1/search?query=to:${destinatario}`);

    if (lista.ok()) {
      const corpo = (await lista.json()) as { messages?: { ID: string }[] };
      for (const mensagemDaLista of corpo.messages ?? []) {
        const detalhe = await api.get(`${MAILPIT}/api/v1/message/${mensagemDaLista.ID}`);
        const mensagem = (await detalhe.json()) as { Text?: string };
        if (mensagem.Text?.includes(caminhoEsperado)) {
          return mensagem.Text;
        }
      }
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error(`Nenhum e-mail chegou para ${destinatario} no Mailpit.`);
}

export function extrairCaminho(corpo: string): string {
  const match = /http:\/\/localhost:4200(\/[^\s]+)/.exec(corpo);
  expect(match, `Nenhum link encontrado no e-mail:\n${corpo}`).not.toBeNull();
  return match![1];
}

/**
 * Conta que o Playwright configura como administrador inicial da API que ele sobe
 * (`PlatformAdministration:InitialAdminEmail`). Fictícia e só local/CI.
 */
export const ADMIN_E2E_EMAIL = 'admin-e2e@exemplo.local';

/** Tenta entrar e diz se deu certo, sem falhar o teste. */
export async function tentarEntrar(page: Page, email: string, senha = SENHA): Promise<boolean> {
  await page.goto('/entrar');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(senha);
  await page.getByRole('button', { name: 'Entrar' }).click();

  // A espera que perde a corrida é encerrada com a página; o catch evita rejeição solta.
  return Promise.race([
    page
      .waitForURL(/\/inicio$/)
      .then(() => true)
      .catch(() => false),
    page
      .getByRole('alert')
      .waitFor()
      .then(() => false)
      .catch(() => false),
  ]);
}

/** Entra e garante que a conta tem o papel de administração da plataforma. */
export async function entrarComoAdmin(page: Page): Promise<boolean> {
  expect(await tentarEntrar(page, ADMIN_E2E_EMAIL), 'A conta de administração não entrou').toBe(
    true,
  );
  const resposta = await page.request.get('/api/v1/auth/me');
  const conta = (await resposta.json()) as { roles: string[] };
  return conta.roles.includes('PlatformAdmin');
}

/** Cadastra, confirma pelo Mailpit e entra. Termina no perfil. */
export async function entrarComContaNova(
  page: Page,
  api: APIRequestContext,
  nome = 'Pessoa E2E',
): Promise<string> {
  const email = emailUnico();

  await page.goto('/cadastro');
  await page.getByLabel('Como quer ser chamado').fill(nome);
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(SENHA);
  await page.getByRole('button', { name: 'Criar minha conta' }).click();
  await expect(page.getByText(/você vai receber uma mensagem/i)).toBeVisible();

  const verificacao = await aguardarEmail(api, email, '/verificar-email');
  await page.goto(extrairCaminho(verificacao));
  await expect(page.getByText('E-mail confirmado')).toBeVisible();

  await page.goto('/entrar');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page).toHaveURL(/\/inicio$/);

  return email;
}
