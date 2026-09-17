import { Page, expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

/** Elenco mínimo do Fut7: titulares da formação e um reserva por posição. */
const ELENCO: readonly (readonly [string, number])[] = [
  ['Goalkeeper', 2],
  ['Defender', 3],
  ['Midfielder', 3],
  ['Forward', 3],
];

test('checklist bloqueia o rascunho vazio e libera a publicação com o catálogo pronto', async ({
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

  await page.goto('/organizar');
  await page
    .getByRole('region', { name: organizacao })
    .getByRole('link', { name: 'Campeonatos' })
    .click();
  await page.getByRole('link', { name: 'Criar campeonato' }).click();
  await page.getByLabel('Nome do campeonato').fill(`Copa Publicável ${Date.now()}`);
  await page.getByRole('radio', { name: /^Fut7/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato' }).click();
  await expect(page).toHaveURL(/\/organizar\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });

  const navegacao = page.getByRole('navigation', { name: 'Navegação do campeonato' });

  // 1. Rascunho vazio: o checklist explica o que falta e não deixa publicar.
  await navegacao.getByRole('link', { name: 'Publicação' }).click();
  await expect(page.getByRole('heading', { name: 'Publicação', level: 1 })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Impedimentos' })).toBeVisible();
  await expect(page.getByText('Crie ao menos uma fase')).toBeVisible();
  await expect(page.getByText(/Cadastre ao menos 2 times ativos/)).toBeVisible();
  await expect(page.getByText(/Faltam atletas na posição goleiro/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Publicar campeonato' })).toBeDisabled();

  // 2. O catálogo é montado pelo caminho real: times, elenco e fase com participantes.
  await navegacao.getByRole('link', { name: 'Times' }).click();
  for (const nome of ['União da Vila', 'Estrela do Bairro']) {
    await page.getByRole('button', { name: 'Adicionar time' }).click();
    await page.getByLabel('Nome do time').fill(nome);
    await page.getByRole('button', { name: 'Adicionar time' }).click();
    await expect(page.getByText(`${nome} adicionado.`)).toBeVisible();
  }

  await navegacao.getByRole('link', { name: 'Atletas' }).click();
  let numero = 0;
  for (const [posicao, quantidade] of ELENCO) {
    for (let indice = 0; indice < quantidade; indice++) {
      // Alternar os times deixa os dois com elenco e mantém o alerta de elenco curto.
      await inscrever(page, `Atleta ${++numero}`, posicao, {
        nivel: indice === 0 ? 'Star' : 'Regular',
        time: numero % 2 === 1 ? 'União da Vila' : 'Estrela do Bairro',
      });
    }
  }

  await navegacao.getByRole('link', { name: 'Fases' }).click();
  await page.getByRole('button', { name: 'Adicionar fase' }).click();
  const nova = page.getByRole('region', { name: 'Nova fase' });
  await nova.getByLabel('Nome da fase').fill('Fase única');
  await nova.getByRole('button', { name: 'Adicionar fase' }).click();
  await expect(page.getByText('Fase única adicionada.')).toBeVisible();

  const fase = page.getByRole('region', { name: '1. Fase única' });
  await fase.getByRole('button', { name: 'Gerenciar times de Fase única' }).click();
  await fase.getByRole('checkbox', { name: 'União da Vila' }).check();
  await fase.getByRole('checkbox', { name: 'Estrela do Bairro' }).check();
  await fase.getByRole('button', { name: 'Salvar times' }).click();
  await expect(page.getByText('Times de Fase única salvos.')).toBeVisible();

  // 3. Com o catálogo pronto, sobram alertas e a publicação é liberada.
  await navegacao.getByRole('link', { name: 'Publicação' }).click();
  await expect(page.getByRole('heading', { name: 'Impedimentos' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Alertas' })).toBeVisible();
  await expect(
    page.getByText(/atletas inscritos; a modalidade pede ao menos 9/).first(),
  ).toBeVisible();

  await page.getByRole('button', { name: 'Publicar campeonato' }).click();
  const publicar = page.getByRole('dialog', { name: 'Publicar o campeonato?' });
  await publicar.getByRole('button', { name: 'Publicar', exact: true }).click();
  await expect(page.getByText('Campeonato publicado.')).toBeVisible();

  // A casca reflete a decisão sem recarregar a página.
  await expect(page.getByText('Publicado', { exact: true }).first()).toBeVisible();

  // 4. Publicado, a modalidade some do formulário de configuração.
  await navegacao.getByRole('link', { name: 'Configuração' }).click();
  await expect(
    page.getByText('O campeonato já foi publicado, então a modalidade não muda mais.'),
  ).toBeVisible();
  await expect(page.getByRole('radio', { name: /^Futsal/ })).toBeDisabled();

  // 5. Voltar para rascunho esconde o campeonato de novo.
  await navegacao.getByRole('link', { name: 'Publicação' }).click();
  await page.getByRole('button', { name: 'Voltar para rascunho' }).click();
  const rascunho = page.getByRole('dialog', { name: 'Voltar para rascunho?' });
  await rascunho.getByRole('button', { name: 'Voltar para rascunho' }).click();
  await expect(page.getByText('Campeonato de volta para rascunho.')).toBeVisible();

  await page.reload();
  await expect(page.getByRole('button', { name: 'Publicar campeonato' })).toBeEnabled();
});

async function inscrever(
  page: Page,
  nome: string,
  posicao: string,
  { nivel, time }: { nivel: string; time: string },
): Promise<void> {
  await page.getByRole('button', { name: 'Adicionar atleta' }).click();
  await page.getByLabel('Nome esportivo').fill(nome);
  await page.getByLabel('Time').selectOption({ label: time });
  await page.getByLabel('Posição').selectOption(posicao);
  await page.getByLabel('Nível de preço').selectOption(nivel);
  await page.getByRole('button', { name: 'Adicionar atleta' }).click();
  await expect(page.getByText(`${nome} adicionado.`)).toBeVisible();
}
