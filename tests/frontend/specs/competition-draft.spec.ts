import { expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

test('proprietário cria campeonato em rascunho, configura e só a organização o acessa', async ({
  browser,
  page,
  request,
}) => {
  const contextoAdmin = await browser.newContext();
  const admin = await contextoAdmin.newPage();
  const ehAdmin = await entrarComoAdmin(admin);
  test.skip(
    !ehAdmin && !process.env['CI'],
    `A API local em execução não tem ${ADMIN_E2E_EMAIL} como administrador inicial.`,
  );
  expect(ehAdmin, 'A conta do E2E deveria ser Platform admin').toBe(true);

  const { organizacao } = await criarOrganizacaoAprovada(page, admin, request);
  const nome = `Copa E2E ${Date.now()}`;

  // 1. Da organização até o formulário de criação.
  await page.goto('/organizar');
  await page
    .getByRole('region', { name: organizacao })
    .getByRole('link', { name: 'Campeonatos' })
    .click();
  await expect(page.getByRole('heading', { name: `Campeonatos de ${organizacao}` })).toBeVisible();
  await expect(page.getByText('Crie o primeiro em rascunho')).toBeVisible();
  await page.getByRole('link', { name: 'Criar campeonato' }).click();

  // 2. Criar em rascunho, escolhendo a modalidade pelo que ela impõe.
  await page.getByLabel('Nome do campeonato').fill(nome);
  await page.getByRole('radio', { name: /^Futsal/ }).check();
  await page
    .getByLabel('Fechamento do mercado')
    .selectOption({ label: '1 hora antes da primeira partida' });
  await page.getByRole('button', { name: 'Criar campeonato' }).click();

  // A primeira abertura da área do campeonato carrega o trecho da aplicação sob demanda.
  await expect(page).toHaveURL(/\/organizar\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });
  await expect(page.getByText('Campeonato criado em rascunho.')).toBeVisible();
  await expect(page).toHaveTitle(`Resumo · ${nome}`);
  const modalidade = page.getByRole('region', { name: 'Modalidade: Futsal' });
  await expect(modalidade.getByText('80 créditos')).toBeVisible();
  await expect(page.getByText('1 hora antes da primeira partida')).toBeVisible();
  const urlDoCampeonato = page.url();

  // 3. Configurar: no rascunho a modalidade ainda muda, e o resumo acompanha.
  const navegacao = page.getByRole('navigation', { name: 'Navegação do campeonato' });
  await navegacao.getByRole('link', { name: 'Configuração' }).click();
  await expect(page.getByRole('heading', { name: 'Configuração', level: 1 })).toBeVisible();
  await page.getByRole('radio', { name: /^Futebol de campo/ }).check();
  await page.getByLabel('Prazo para publicar o resultado (dias úteis)').fill('4');
  await page.getByRole('button', { name: 'Salvar configuração' }).click();
  await expect(page.getByText('Configuração salva.')).toBeVisible();

  await navegacao.getByRole('link', { name: 'Resumo' }).click();
  await expect(page.getByRole('region', { name: 'Modalidade: Futebol de campo' })).toBeVisible();
  await expect(page.getByText('135 créditos')).toBeVisible();
  await expect(page.getByText('até 4 dias úteis')).toBeVisible();

  // 4. Duas abas sobre a mesma leitura: a segunda a salvar não sobrescreve a primeira.
  const outraAba = await page.context().newPage();
  await outraAba.goto(`${urlDoCampeonato}/configuracao`);
  await page.goto(`${urlDoCampeonato}/configuracao`);
  await page.getByLabel('Nome do campeonato').fill(`${nome} (aba 1)`);
  await page.getByRole('button', { name: 'Salvar configuração' }).click();
  await expect(page.getByText('Configuração salva.')).toBeVisible();

  await outraAba.getByLabel('Nome do campeonato').fill(`${nome} (aba 2)`);
  await outraAba.getByRole('button', { name: 'Salvar configuração' }).click();
  await expect(
    outraAba.getByText('Alguém salvou esta configuração enquanto você editava.'),
  ).toBeVisible();
  await outraAba.getByRole('button', { name: 'Carregar versão atual' }).click();
  await expect(outraAba.getByLabel('Nome do campeonato')).toHaveValue(`${nome} (aba 1)`);
  await outraAba.close();

  // 5. Quem não é da organização não abre o campeonato, nem a administração da plataforma.
  await admin.goto(urlDoCampeonato);
  await expect(
    admin.getByText('Este campeonato não existe ou não pertence a uma organização sua.'),
  ).toBeVisible();
  await expect(admin.getByText(nome)).toHaveCount(0);

  await contextoAdmin.close();
});
