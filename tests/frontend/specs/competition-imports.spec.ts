import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { Page, expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

/** Massa fictícia versionada no repositório, a mesma que a demonstração usa. */
const DADOS_DEMO = join(
  dirname(fileURLToPath(import.meta.url)),
  '..',
  '..',
  '..',
  'infra',
  'dados-demo',
);

test('proprietário confere a planilha antes de importar e vê os erros por linha', async ({
  browser,
  page,
  request,
}) => {
  test.slow();
  const contextoAdmin = await browser.newContext();
  const admin = await contextoAdmin.newPage();
  const ehAdmin = await entrarComoAdmin(admin);
  test.skip(
    !ehAdmin && !process.env['CI'],
    `A API local em execução não tem ${ADMIN_E2E_EMAIL} como administrador inicial.`,
  );
  expect(ehAdmin, 'A conta do E2E deveria ser Platform admin').toBe(true);

  const { organizacao } = await criarOrganizacaoAprovada(page, admin, request);
  await contextoAdmin.close();

  await criarCampeonato(page, organizacao);
  const navegacao = page.getByRole('navigation', { name: 'Navegação do campeonato' });
  await navegacao.getByRole('link', { name: 'Importações' }).click();
  await expect(page.getByRole('heading', { name: 'Importações', level: 1 })).toBeVisible();

  // 1. O modelo documenta as colunas e baixa como arquivo de verdade.
  await expect(page.getByText('Modelo times-v1.csv, versão 1.')).toBeVisible();
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.getByRole('link', { name: 'Baixar modelo de times' }).click(),
  ]);
  expect(download.suggestedFilename()).toBe('times-v1.csv');

  // 2. Conferir mostra a prévia e deixa claro que nada foi gravado.
  await enviar(page, 'times-v1.csv');
  await expect(page.getByRole('button', { name: 'Importar' })).toBeDisabled();
  await page.getByRole('button', { name: 'Conferir arquivo' }).click();
  await expect(
    page.getByText('6 linhas lidas: 6 a criar, 0 a alterar, 0 sem mudança.'),
  ).toBeVisible();
  await expect(page.getByText('Nada foi gravado ainda.')).toBeVisible();

  // 3. Importar aplica, e o catálogo passa a ter os times.
  await page.getByRole('button', { name: 'Importar' }).click();
  await expect(
    page.getByText('6 linhas lidas: 6 criados, 0 alterados, 0 sem mudança.'),
  ).toBeVisible();

  await navegacao.getByRole('link', { name: 'Times' }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'União da Vila' })).toBeVisible();
  await expect(page.getByRole('listitem').filter({ hasText: 'Real Mangueiral' })).toBeVisible();

  // 4. Reenviar o mesmo arquivo não duplica nada.
  await navegacao.getByRole('link', { name: 'Importações' }).click();
  await enviar(page, 'times-v1.csv');
  await page.getByRole('button', { name: 'Conferir arquivo' }).click();
  await expect(
    page.getByText('6 linhas lidas: 0 a criar, 0 a alterar, 6 sem mudança.'),
  ).toBeVisible();

  // 5. Atleta apontando para um time que não existe: erro na linha, nada gravado.
  await page.getByRole('tab', { name: 'Atletas' }).click();
  await expect(page.getByText('Modelo atletas-v1.csv')).toBeVisible();
  await enviarConteudo(
    page,
    'atletas.csv',
    'nome_esportivo;time;posicao;nivel_preco;preco_exato\r\n' +
      'Bia;União da Vila;meio-campista;destaque;\r\n' +
      'Nena;Time Fantasma;goleiro;;\r\n',
  );
  await page.getByRole('button', { name: 'Conferir arquivo' }).click();
  await expect(page.getByText('Linha 3')).toBeVisible();
  await expect(page.getByText('Time Fantasma')).toBeVisible();
  await expect(page.getByText('Nada foi gravado.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Importar' })).toBeDisabled();

  // 6. Com o arquivo corrigido, o elenco fictício inteiro entra de uma vez.
  await enviar(page, 'atletas-v1.csv');
  await page.getByRole('button', { name: 'Conferir arquivo' }).click();
  await expect(
    page.getByText('54 linhas lidas: 54 a criar, 0 a alterar, 0 sem mudança.'),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Importar' }).click();
  await expect(
    page.getByText('54 linhas lidas: 54 criados, 0 alterados, 0 sem mudança.'),
  ).toBeVisible();

  await navegacao.getByRole('link', { name: 'Atletas' }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'Maestro' }).first()).toContainText(
    'Estrela do Bairro',
  );
});

async function criarCampeonato(page: Page, organizacao: string): Promise<void> {
  await page.goto('/organizar');
  await page
    .getByRole('region', { name: organizacao })
    .getByRole('link', { name: 'Campeonatos' })
    .click();
  await page.getByRole('link', { name: 'Criar campeonato' }).click();
  await page.getByLabel('Nome do campeonato').fill(`Copa Importada ${Date.now()}`);
  await page.getByRole('radio', { name: /^Fut7/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato' }).click();
  await expect(page).toHaveURL(/\/organizar\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });
}

async function enviar(page: Page, fileName: string): Promise<void> {
  await page.getByLabel('Arquivo CSV').setInputFiles(join(DADOS_DEMO, fileName));
  await expect(page.getByText(`Escolhido: ${fileName}`)).toBeVisible();
}

async function enviarConteudo(page: Page, fileName: string, conteudo: string): Promise<void> {
  await page.getByLabel('Arquivo CSV').setInputFiles({
    name: fileName,
    mimeType: 'text/csv',
    buffer: Buffer.from(conteudo, 'utf8'),
  });
  await expect(page.getByText(`Escolhido: ${fileName}`)).toBeVisible();
}

test('os arquivos de demonstração estão no repositório e são legíveis', () => {
  // Guarda barata: se alguém renomear a pasta, o spec acima falharia sem explicar por quê.
  for (const arquivo of ['times-v1.csv', 'atletas-v1.csv', 'tecnicos-v1.csv']) {
    expect(readFileSync(join(DADOS_DEMO, arquivo)).byteLength).toBeGreaterThan(0);
  }
});
