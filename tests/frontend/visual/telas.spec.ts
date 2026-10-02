import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { expect, test, type Page } from '@playwright/test';

/**
 * Oito telas-chave do redesign, em desktop e celular, comparadas com a referência.
 *
 * Sem `GRAVAR`, a API é respondida pelos dados de `dados/`, gravados da demonstração, e o
 * relógio para no instante da gravação. Com `GRAVAR=1`, as mesmas telas são abertas
 * contra a API local da demonstração (porta 5277) e as respostas são regravadas; as
 * senhas das contas da demo vêm do ambiente, nunca do repositório.
 */
const AQUI = dirname(fileURLToPath(import.meta.url));
const DADOS = join(AQUI, 'dados');
const GRAVAR = !!process.env['GRAVAR'];
const SLUG = 'copa-da-vila-2026';

type Conta = 'anonimo' | 'visitante' | 'organizacao';
type Resposta = { status: number; corpo: unknown };
type Gravacao = { relogio: string; respostas: Record<string, Resposta> };

const CONTAS: Record<Exclude<Conta, 'anonimo'>, { email: string; senha: string }> = {
  visitante: { email: 'visitante@demo.cartola-varzea.test', senha: 'DEMO_VIEWER_PASSWORD' },
  organizacao: { email: 'organizacao@demo.cartola-varzea.test', senha: 'DEMO_ORGANIZER_PASSWORD' },
};

const TELAS: readonly { nome: string; conta: Conta; rota: string }[] = [
  { nome: 'landing', conta: 'anonimo', rota: '/' },
  { nome: 'inicio', conta: 'visitante', rota: '/inicio' },
  { nome: 'meu-time', conta: 'visitante', rota: `/c/${SLUG}/escalacao` },
  { nome: 'mercado', conta: 'visitante', rota: `/c/${SLUG}/mercado` },
  { nome: 'pontuacao', conta: 'visitante', rota: 'pontuacao' },
  { nome: 'classificacao', conta: 'visitante', rota: `/c/${SLUG}/ranking` },
  { nome: 'central-da-rodada', conta: 'organizacao', rota: 'central-da-rodada' },
  { nome: 'sumula', conta: 'organizacao', rota: 'sumula' },
];

const ler = <T>(arquivo: string, padrao: T): T =>
  existsSync(join(DADOS, arquivo))
    ? (JSON.parse(readFileSync(join(DADOS, arquivo), 'utf8')) as T)
    : padrao;

const gravar = (arquivo: string, dados: unknown): void => {
  mkdirSync(DADOS, { recursive: true });
  writeFileSync(join(DADOS, arquivo), `${JSON.stringify(dados, null, 2)}\n`);
};

/** As rotas com identificador: Rodada 3 apurada, Rodada 4 em revisão e a 1ª partida dela. */
async function gravarRotas(page: Page, conta: Conta): Promise<void> {
  const rotas = ler<Record<string, string>>('rotas.json', {});
  if (conta === 'visitante') {
    const calendario = (await (
      await page.request.get(`/api/v1/public/competitions/${SLUG}/fixtures`)
    ).json()) as { rounds: { id: string; name: string }[] };
    rotas['pontuacao'] =
      `/c/${SLUG}/pontuacao/${calendario.rounds.find((r) => r.name === 'Rodada 3')!.id}`;
  }
  if (conta === 'organizacao') {
    const [liga] = (await (await page.request.get('/api/v1/organizations/mine')).json()) as {
      id: string;
    }[];
    const [copa] = (await (
      await page.request.get(`/api/v1/organizations/${liga!.id}/competitions`)
    ).json()) as { id: string }[];
    const rodadas = (await (
      await page.request.get(`/api/v1/competitions/${copa!.id}/rounds`)
    ).json()) as { id: string; name: string; matches: { id: string }[] }[];
    const quarta = rodadas.find((r) => r.name === 'Rodada 4')!;
    rotas['central-da-rodada'] = `/organizar/c/${copa!.id}/rodadas/${quarta.id}/revisao`;
    rotas['sumula'] = `/organizar/c/${copa!.id}/partidas/${quarta.matches[0]!.id}/sumula`;
  }
  gravar('rotas.json', rotas);
}

for (const tela of TELAS) {
  test(tela.nome, async ({ page }) => {
    const arquivo = `${tela.conta}.json`;
    const gravacao = ler<Gravacao>(arquivo, { relogio: new Date().toISOString(), respostas: {} });
    const faltando: string[] = [];

    await page.route('**/api/**', async (route) => {
      const pedido = route.request();
      const url = new URL(pedido.url());
      const chave = `${pedido.method()} ${url.pathname}${url.search}`;
      if (GRAVAR) {
        if (pedido.method() !== 'GET') return route.continue();
        const resposta = await route.fetch();
        const texto = await resposta.text();
        gravacao.respostas[chave] = {
          status: resposta.status(),
          corpo: texto ? (JSON.parse(texto) as unknown) : null,
        };
        return route.fulfill({ response: resposta, body: texto });
      }
      const gravada = gravacao.respostas[chave];
      if (!gravada) {
        faltando.push(chave);
        return route.fulfill({ status: 404, contentType: 'application/problem+json', body: '{}' });
      }
      return route.fulfill({
        status: gravada.status,
        contentType: 'application/json',
        body: gravada.corpo === null ? '' : JSON.stringify(gravada.corpo),
      });
    });

    if (GRAVAR && tela.conta !== 'anonimo') {
      const conta = CONTAS[tela.conta];
      await page.goto('/entrar');
      await page.getByLabel('E-mail').fill(conta.email);
      await page.getByLabel('Senha').fill(process.env[conta.senha] ?? '');
      await page.getByRole('button', { name: 'Entrar', exact: true }).click();
      await page.waitForURL(/inicio/);
      await gravarRotas(page, tela.conta);
    }
    if (!GRAVAR) await page.clock.setFixedTime(new Date(gravacao.relogio));

    const rota = tela.rota.startsWith('/')
      ? tela.rota
      : ler<Record<string, string>>('rotas.json', {})[tela.rota]!;
    await page.goto(rota);
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
    await page.waitForLoadState('networkidle');
    await expect(page.locator('app-loading')).toHaveCount(0);
    await page.evaluate(() => document.fonts.ready);

    if (GRAVAR) {
      gravar(arquivo, gravacao);
      return;
    }
    expect(faltando, 'Respostas que a gravação não tem').toEqual([]);
    await expect(page).toHaveScreenshot(`${tela.nome}.png`, { fullPage: true });
  });
}
