// Capturas do roteiro da demonstração (01 §13.1), passo a passo, numa largura.
//
// Uso, em tests/frontend, com a demo recém-recriada, a API dela no ar com a entrada de
// visitante ligada e o frontend no ar:
//   $env:DEMO_ORGANIZER_PASSWORD = '<do .env>'; node demo/capturas.mjs 1440 [pasta de saída]
//   $env:DEMO_ORGANIZER_PASSWORD = '<do .env>'; node demo/capturas.mjs 412 [pasta de saída]
//
// Só lê: o diálogo de publicação é aberto e não confirmado.
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';

import { chromium } from '@playwright/test';

const [, , largura = '1440', saida = 'demo/saida/capturas'] = process.argv;
const BASE = process.env['BASE_URL'] ?? 'http://localhost:4200';
const API = `${BASE}/api/v1`;
const SLUG = 'copa-da-vila-2026';
const senhaDaOrganizacao = process.env['DEMO_ORGANIZER_PASSWORD'];
if (!senhaDaOrganizacao) throw new Error('Defina DEMO_ORGANIZER_PASSWORD com o valor do .env.');
const pasta = join(saida, largura);
mkdirSync(pasta, { recursive: true });

const celular = Number(largura) < 600;
const browser = await chromium.launch();
const nova = async () => {
  const contexto = await browser.newContext({
    viewport: { width: Number(largura), height: celular ? 915 : 900 },
    deviceScaleFactor: celular ? 2 : 1,
    isMobile: celular,
    hasTouch: celular,
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
  });
  const page = await contexto.newPage();
  await page.emulateMedia({ reducedMotion: 'reduce' });
  return page;
};

async function foto(page, nome, url, antes) {
  if (url) await page.goto(BASE + url);
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(500);
  if (antes) await antes(page);
  const inteira = !nome.includes('sino') && !nome.includes('dialogo');
  await page.screenshot({ path: join(pasta, `${nome}.png`), fullPage: inteira });
  console.log('ok', nome);
}

const calendario = await (await fetch(`${API}/public/competitions/${SLUG}/fixtures`)).json();
const rodada = (nome) => calendario.rounds.find((item) => item.name === nome);
const goleada = rodada('Rodada 3').matches.find((jogo) => jogo.homeScore === 5);

// Ato 1 — visitante sem conta.
const anonimo = await nova();
await foto(anonimo, '01-landing', '/');
await foto(anonimo, '02-campeonatos', '/campeonatos');
await foto(anonimo, '03-campeonato', `/c/${SLUG}`);
await foto(anonimo, '04-partidas', `/c/${SLUG}/partidas`);
await foto(anonimo, '05-sumula', `/c/${SLUG}/partidas/${goleada.id}`);
await foto(anonimo, '06-tabela', `/c/${SLUG}/tabela`);
await foto(anonimo, '07-ranking', `/c/${SLUG}/ranking`);
await foto(anonimo, '08-regras', '/regras');
await foto(anonimo, 'erro-campeonato-inexistente', '/c/campeonato-que-nao-existe-2026');
await foto(anonimo, '09-entrar', '/entrar');

// Ato 2 — o jogo, pela conta pública de visitante (somente leitura).
const visitante = await nova();
await visitante.goto(`${BASE}/entrar`);
await visitante.getByRole('button', { name: 'Entrar como visitante' }).click();
await visitante.waitForURL(/inicio/);
await foto(visitante, '10-inicio', '/inicio');
await foto(visitante, '11-meu-time', `/c/${SLUG}/escalacao`);
await foto(visitante, '12-mercado', `/c/${SLUG}/mercado`);
await foto(visitante, '13-rodadas', `/c/${SLUG}/rodadas`);
await foto(visitante, '14-pontuacao-corrigida', `/c/${SLUG}/pontuacao/${rodada('Rodada 2').id}`);
await foto(visitante, 'vazio-rodada-sem-apuracao', `/c/${SLUG}/pontuacao/${rodada('Rodada 4').id}`);
await foto(visitante, '15-classificacao', `/c/${SLUG}/ranking`);
await foto(visitante, '16-liga', `/c/${SLUG}/ligas`, async (page) => {
  await page
    .getByRole('link', { name: /Liga da Firma/ })
    .first()
    .click();
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(400);
});
await foto(visitante, '17-sino', '/inicio', async (page) => {
  await page.getByRole('button', { name: /^Avisos/ }).click();
  await page.waitForTimeout(300);
});

// Atos 3 e 4 — organização e apuração, pela conta da organização.
const organizacao = await nova();
await organizacao.goto(`${BASE}/entrar`);
await organizacao.getByLabel('E-mail').fill('organizacao@demo.cartola-varzea.test');
await organizacao.getByLabel('Senha').fill(senhaDaOrganizacao);
await organizacao.getByRole('button', { name: 'Entrar', exact: true }).click();
await organizacao.waitForURL(/inicio/);
const [liga] = await (await organizacao.request.get(`${API}/organizations/mine`)).json();
const campeonatos = await (
  await organizacao.request.get(`${API}/organizations/${liga.id}/competitions`)
).json();
const copa = campeonatos[0].id;
const rodadas = await (await organizacao.request.get(`${API}/competitions/${copa}/rounds`)).json();
const quarta = rodadas.find((item) => item.name === 'Rodada 4');
const segunda = rodadas.find((item) => item.name === 'Rodada 2');
await foto(organizacao, '18-organizar', '/organizar');
await foto(organizacao, '19-resumo-do-campeonato', `/organizar/c/${copa}`);
await foto(organizacao, '20-rodadas-da-organizacao', `/organizar/c/${copa}/rodadas`);
await foto(
  organizacao,
  '21-sumula-em-edicao',
  `/organizar/c/${copa}/partidas/${quarta.matches[0].id}/sumula`,
);
await foto(
  organizacao,
  '22-central-da-rodada',
  `/organizar/c/${copa}/rodadas/${quarta.id}/revisao`,
);
await foto(
  organizacao,
  '23-dialogo-publicar',
  `/organizar/c/${copa}/rodadas/${quarta.id}/revisao`,
  async (page) => {
    await page.getByRole('button', { name: 'Publicar resultado' }).click();
    await page.waitForTimeout(300);
  },
);
await foto(
  organizacao,
  '24-rodada-corrigida',
  `/organizar/c/${copa}/rodadas/${segunda.id}/revisao`,
);

await browser.close();
