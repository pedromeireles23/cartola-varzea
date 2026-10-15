import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

import { APIRequestContext, Page, expect } from '@playwright/test';

import { codigoTotp } from './totp';

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

/**
 * A chave do aplicativo autenticador do admin do E2E, que o setup guarda ao ligar os dois
 * fatores (Fase 20). Fica fora do Git, junto com o banco do E2E que ela acompanha.
 */
export const ADMIN_E2E_CHAVE = new URL('../.auth/admin-e2e-autenticador.txt', import.meta.url);

/** Como terminou a primeira etapa da entrada. */
export type Entrada = 'entrou' | 'codigo' | 'recusada';

/** E-mail e senha na tela de entrada, sem falhar o teste. */
export async function enviarSenha(page: Page, email: string, senha = SENHA): Promise<Entrada> {
  await page.goto('/entrar');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(senha);
  await page.getByRole('button', { name: 'Entrar' }).click();

  // A espera que perde a corrida é encerrada com a página; o catch evita rejeição solta.
  return Promise.race<Entrada>([
    page
      .waitForURL(/\/inicio$/)
      .then(() => 'entrou' as const)
      .catch(() => 'recusada' as const),
    page
      .getByRole('textbox', { name: 'Código' })
      .waitFor()
      .then(() => 'codigo' as const)
      .catch(() => 'recusada' as const),
    page
      .getByRole('alert')
      .waitFor()
      .then(() => 'recusada' as const)
      .catch(() => 'recusada' as const),
  ]);
}

/** Tenta entrar e diz se deu certo, sem falhar o teste. */
export async function tentarEntrar(page: Page, email: string, senha = SENHA): Promise<boolean> {
  return (await enviarSenha(page, email, senha)) === 'entrou';
}

/** O segundo passo da entrada, com o código do aplicativo calculado da chave. */
export async function digitarCodigo(page: Page, chave: string): Promise<void> {
  await page.getByRole('textbox', { name: 'Código' }).fill(codigoTotp(chave));
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page).toHaveURL(/\/inicio$/);
}

export async function lerChaveDoAdmin(): Promise<string> {
  try {
    return (await readFile(ADMIN_E2E_CHAVE, 'utf8')).trim();
  } catch {
    throw new Error(
      `${ADMIN_E2E_EMAIL} já tem dois fatores, mas a chave não está em ` +
        `${fileURLToPath(ADMIN_E2E_CHAVE)}. Apague essa conta do banco Fut7Fantasy_E2E ` +
        '(ou recrie o banco) para o setup ligar os dois fatores de novo.',
    );
  }
}

export async function guardarChaveDoAdmin(chave: string): Promise<void> {
  await mkdir(new URL('.', ADMIN_E2E_CHAVE), { recursive: true });
  await writeFile(ADMIN_E2E_CHAVE, chave, 'utf8');
}

/** Entra com senha e código do aplicativo, e diz se a conta tem o papel de administração. */
export async function entrarComoAdmin(page: Page): Promise<boolean> {
  const entrada = await enviarSenha(page, ADMIN_E2E_EMAIL);
  if (entrada === 'codigo') {
    await digitarCodigo(page, await lerChaveDoAdmin());
  } else {
    expect(entrada, 'A conta de administração não entrou').toBe('entrou');
  }

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
