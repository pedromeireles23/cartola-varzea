import { expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

/** Dez atletas por time, com as posições escritas como numa lista de WhatsApp. */
const POSICOES = [
  'goleiro',
  'goleiro',
  'zagueiro',
  'lateral',
  'zagueiro',
  'meia',
  'volante',
  'meia',
  'atacante',
  'atacante',
];

test('proprietário monta o campeonato inteiro pela tela, sem planilha, e publica', async ({
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
  await contextoAdmin.close();
  const marca = Date.now();

  await page.goto('/organizar');
  await page
    .getByRole('region', { name: organizacao })
    .getByRole('link', { name: 'Campeonatos' })
    .click();
  await page.getByRole('link', { name: 'Criar campeonato' }).click();
  await page.getByLabel('Nome do campeonato').fill(`Copa Montada ${marca}`);
  await page.getByRole('radio', { name: /^Fut7/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato' }).click();

  // 1. Times: a lista colada vira a grade, com o técnico depois do traço.
  await expect(page).toHaveURL(/\/montar\/times$/, { timeout: 15_000 });
  const passos = page.getByRole('navigation', { name: 'Passos da montagem' });
  await expect(passos.getByRole('link', { name: /1\. Times/ })).toHaveAttribute(
    'aria-current',
    'step',
  );
  const times = ['União', 'Estrela', 'Grêmio', 'Juventude'].map((nome) => `${nome} ${marca}`);
  await page.getByRole('button', { name: 'Colar uma lista' }).click();
  await page
    .getByLabel(/Um time por linha/)
    .fill(`${times[0]} - Seu Zé\n${times.slice(1).join('\n')}`);
  await page.getByRole('button', { name: 'Pôr na grade' }).click();
  await expect(page.getByLabel('Técnico do time novo 1')).toHaveValue('Seu Zé');
  await page.getByRole('button', { name: 'Salvar times' }).click();
  await expect(page.getByText('4 times adicionados.')).toBeVisible();
  await expect(page.getByLabel(`Técnico de ${times[0]}`)).toHaveValue('Seu Zé');
  await expect(passos.getByRole('link', { name: /1\. Times.*pronto/ })).toBeVisible();

  // 2. Elencos: a linha sem posição volta marcada, e nada é gravado até corrigir.
  await page.getByRole('link', { name: 'Próximo: Elencos' }).click();
  for (const [indice, time] of times.slice(0, 2).entries()) {
    await page.getByRole('button', { name: new RegExp(`^${time} \\d+ de`) }).click();
    await page.getByRole('button', { name: 'Colar uma lista' }).click();
    const lista = POSICOES.map(
      (posicao, n) => `${n + 1}. Atleta ${marca}-${indice}${n} - ${posicao}`,
    );
    if (indice === 0) lista.push(`Tanque de Gol ${marca}`);
    await page.getByLabel(/Um atleta por linha/).fill(lista.join('\n'));
    await page.getByRole('button', { name: 'Pôr na grade' }).click();

    if (indice === 0) {
      await expect(page.getByText('Escolha a posição de 1 atleta.')).toBeVisible();
      await page.getByRole('button', { name: `Salvar elenco de ${time}` }).click();
      await expect(page.getByText('Escolha a posição.')).toBeVisible();
      await page.getByLabel('Posição do atleta novo 11').selectOption('Forward');
    }

    await page.getByRole('button', { name: `Salvar elenco de ${time}` }).click();
    await expect(page.getByText(`Elenco de ${time} salvo.`)).toBeVisible();
  }
  await expect(
    page.getByRole('button', { name: new RegExp(`^${times[0]} 11 de 9`) }),
  ).toBeVisible();

  // 3. Formato: pontos corridos vira uma fase de grupo único com todos os times.
  await page.getByRole('link', { name: 'Próximo: Formato' }).click();
  await page.getByRole('radio', { name: /^Pontos corridos/ }).check();
  await page.getByRole('button', { name: 'Usar este formato' }).click();
  await expect(page.getByText('Formato definido.')).toBeVisible();
  await expect(page.getByText('Grupo único · 4 times')).toBeVisible();

  // 4. Tabela: quatro times dão seis jogos em três rodadas; salvar cria tudo em rascunho.
  await page.getByRole('link', { name: 'Próximo: Tabela' }).click();
  await expect(page.getByRole('heading', { name: 'Prévia: 6 jogos em 3 rodadas' })).toBeVisible();
  await page.getByLabel('Turnos').selectOption('2');
  await expect(page.getByRole('heading', { name: 'Prévia: 12 jogos em 6 rodadas' })).toBeVisible();
  await page.getByRole('button', { name: 'Salvar a tabela de Pontos corridos' }).click();
  await expect(page.getByText('12 jogos criados em 6 rodadas, em rascunho.')).toBeVisible();
  await expect(passos.getByRole('link', { name: /4\. Tabela.*12 jogos/ })).toBeVisible();

  // 5. Publicar: os alertas não seguram, e a publicação acontece ali mesmo.
  await page.getByRole('link', { name: 'Próximo: Publicar' }).click();
  await expect(page.getByText(/Alerta: .+ atletas inscritos/).first()).toBeVisible();
  await page.getByRole('button', { name: 'Publicar o campeonato' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publicar' }).click();
  await expect(page.getByText('Campeonato publicado.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Ver a página pública' })).toBeVisible();

  // As rodadas geradas estão na tela de sempre, em rascunho.
  await page
    .getByRole('navigation', { name: 'Navegação do campeonato' })
    .getByRole('link', { name: 'Rodadas' })
    .click();
  await expect(page.getByRole('region', { name: '6. Rodada 6' })).toBeVisible();
});
