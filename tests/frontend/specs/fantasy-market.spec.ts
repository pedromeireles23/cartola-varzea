import { Page, expect, test } from '@playwright/test';

import { ADMIN_E2E_EMAIL, entrarComContaNova, entrarComoAdmin } from '../support/conta';
import { criarOrganizacaoAprovada } from '../support/organizacao';

/** Elenco mínimo do Fut7: titulares da formação e um reserva por posição. */
const ELENCO: readonly (readonly [string, number])[] = [
  ['Goalkeeper', 2],
  ['Defender', 3],
  ['Midfielder', 3],
  ['Forward', 3],
];

test('participante entra pela página pública, compra e vende no mercado aberto', async ({
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

  const marca = `Mercado${Date.now()}`;
  const endereco = await prepararCampeonato(page, organizacao, `Copa ${marca}`);
  const slug = endereco.replace('/c/', '');

  // A partir daqui é quem joga: outra conta, em outro contexto.
  const contextoJogador = await browser.newContext();
  const jogador = await contextoJogador.newPage();
  await entrarComContaNova(jogador, request, 'Pessoa Jogadora');

  await jogador.goto(endereco);
  await jogador.getByRole('link', { name: 'Jogar neste campeonato' }).click();
  await expect(jogador).toHaveURL(new RegExp(`/c/${slug}/jogar$`));
  await expect(jogador.getByText('Ao entrar você recebe C$ 100,00')).toBeVisible();
  await expect(jogador.getByText(/Rodada 1 · fecha .* \(Horário de Brasília\)/)).toBeVisible();

  await jogador.getByRole('button', { name: 'Entrar no campeonato' }).click();
  await expect(jogador.getByText('Você entrou no campeonato e recebeu C$ 100,00.')).toBeVisible();
  await expect(jogador.getByText('Falta o técnico.')).toBeVisible();

  await jogador.getByRole('link', { name: 'Abrir o mercado' }).click();
  await expect(jogador.getByRole('heading', { name: 'Mercado', level: 1 })).toBeVisible();
  await expect(jogador.getByRole('heading', { name: 'União da Vila', level: 2 })).toBeVisible();
  await expect(jogador.getByRole('heading', { name: 'Estrela do Bairro', level: 2 })).toBeVisible();

  await jogador.getByLabel('Posição').selectOption({ label: 'Goleiro' });
  await expect(jogador.getByText('2 opções em 2 times')).toBeVisible();

  await jogador.getByRole('button', { name: 'Comprar Atleta 1', exact: true }).click();
  await expect(
    jogador.getByRole('status').filter({ hasText: 'Atleta 1 entrou no seu elenco.' }),
  ).toHaveCount(1);
  await expect(jogador.getByText('No seu elenco: 1 de')).toBeVisible();
  await expect(jogador.getByRole('button', { name: 'Vender Atleta 1', exact: true })).toBeVisible();

  // Recarregar prova que a compra está no servidor, não só na tela.
  await jogador.reload();
  await expect(jogador.getByRole('button', { name: 'Vender Atleta 1', exact: true })).toBeVisible();

  await jogador.getByRole('button', { name: 'Vender Atleta 1', exact: true }).click();
  await expect(
    jogador
      .getByRole('status')
      .filter({ hasText: 'Atleta 1 saiu do seu elenco. Saldo: C$ 100,00.' }),
  ).toHaveCount(1);
  await expect(
    jogador.getByRole('button', { name: 'Comprar Atleta 1', exact: true }),
  ).toBeVisible();
  await contextoJogador.close();
});

/**
 * Campeonato de Fut7 publicado com a Rodada 1 marcada e o mercado aberto. Devolve o
 * endereço público.
 */
async function prepararCampeonato(page: Page, organizacao: string, nome: string): Promise<string> {
  await page.goto('/organizar');
  await page
    .getByRole('region', { name: organizacao })
    .getByRole('link', { name: 'Campeonatos' })
    .click();
  await page.getByRole('link', { name: 'Criar campeonato' }).click();
  await page.getByLabel('Nome do campeonato').fill(nome);
  await page.getByRole('radio', { name: /^Fut7/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato' }).click();
  await expect(page).toHaveURL(/\/organizar\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });
  const navegacao = page.getByRole('navigation', { name: 'Navegação do campeonato' });

  await navegacao.getByRole('link', { name: 'Times' }).click();
  for (const time of ['União da Vila', 'Estrela do Bairro']) {
    await page.getByRole('button', { name: 'Adicionar time' }).click();
    await page.getByLabel('Nome do time').fill(time);
    await page.getByRole('button', { name: 'Adicionar time' }).click();
    await expect(page.getByText(`${time} adicionado.`)).toBeVisible();
  }

  await navegacao.getByRole('link', { name: 'Atletas' }).click();
  let numero = 0;
  for (const [posicao, quantidade] of ELENCO) {
    for (let indice = 0; indice < quantidade; indice++) {
      numero++;
      const atleta = `Atleta ${numero}`;
      await page.getByRole('button', { name: 'Adicionar atleta' }).click();
      await page.getByLabel('Nome esportivo').fill(atleta);
      await page
        .getByLabel('Time')
        .selectOption({ label: numero % 2 === 1 ? 'União da Vila' : 'Estrela do Bairro' });
      await page.getByLabel('Posição').selectOption(posicao);
      await page.getByRole('button', { name: 'Adicionar atleta' }).click();
      await expect(page.getByText(`${atleta} adicionado.`)).toBeVisible();
    }
  }

  await navegacao.getByRole('link', { name: 'Fases' }).click();
  await page.getByRole('button', { name: 'Adicionar fase' }).click();
  const nova = page.getByRole('region', { name: 'Nova fase' });
  await nova.getByLabel('Nome da fase').fill('Fase de grupos');
  await nova.getByRole('button', { name: 'Adicionar fase' }).click();
  await expect(page.getByText('Fase de grupos adicionada.')).toBeVisible();
  const fase = page.getByRole('region', { name: '1. Fase de grupos' });
  await fase.getByRole('button', { name: 'Gerenciar times de Fase de grupos' }).click();
  await fase.getByRole('checkbox', { name: 'União da Vila' }).check();
  await fase.getByRole('checkbox', { name: 'Estrela do Bairro' }).check();
  await fase.getByRole('button', { name: 'Salvar times' }).click();
  await expect(page.getByText('Times de Fase de grupos salvos.')).toBeVisible();

  await navegacao.getByRole('link', { name: 'Publicação' }).click();
  await page.getByRole('button', { name: 'Publicar campeonato' }).click();
  await page
    .getByRole('dialog', { name: 'Publicar o campeonato?' })
    .getByRole('button', { name: 'Publicar', exact: true })
    .click();
  await expect(page.getByText('Campeonato publicado.')).toBeVisible();
  const link = page.getByRole('link', { name: /^\/c\// });
  const endereco = (await link.textContent())!.trim();

  await navegacao.getByRole('link', { name: 'Rodadas' }).click();
  await page.getByRole('button', { name: 'Adicionar rodada' }).click();
  await page.getByLabel('Nome da rodada').fill('Rodada 1');
  await page.getByRole('button', { name: 'Adicionar rodada' }).click();
  await expect(page.getByText('Rodada 1 criada.')).toBeVisible();
  const rodada = page.getByRole('region', { name: '1. Rodada 1' });
  await rodada.getByRole('button', { name: 'Adicionar partida em Rodada 1' }).click();
  await rodada.getByLabel('Mandante').selectOption({ label: 'União da Vila — Grupo A' });
  await rodada.getByLabel('Visitante').selectOption({ label: 'Estrela do Bairro — Grupo A' });
  await rodada.getByLabel('Data e hora').fill(daquiADezDias());
  await rodada.getByRole('button', { name: 'Adicionar partida' }).click();
  await expect(page.getByText('Partida adicionada.')).toBeVisible();
  await rodada.getByRole('button', { name: 'Abrir mercado de Rodada 1' }).click();
  await expect(page.getByText('Mercado de Rodada 1 aberto.')).toBeVisible();

  return endereco;
}

/** Precisa estar no futuro: o servidor recusa abrir um mercado que já teria fechado. */
function daquiADezDias(): string {
  const data = new Date();
  data.setDate(data.getDate() + 10);
  const mes = String(data.getMonth() + 1).padStart(2, '0');
  const dia = String(data.getDate()).padStart(2, '0');
  return `${data.getFullYear()}-${mes}-${dia}T10:00`;
}
