// Vídeo do roteiro da demonstração (01 §13.1), gravado pelo Playwright.
//
// Uso, em tests/frontend, com a demo recém-recriada (infra/scripts/demo-reset.ps1), a API
// apontando para ela com a entrada de visitante ligada e o frontend no ar:
//   $env:DEMO_ORGANIZER_PASSWORD = '<do .env>'; node demo/video.mjs [pasta de saída]
//
// O ato da apuração publica a Rodada 4 de verdade: recrie a demo depois de gravar.
import { execFileSync } from 'node:child_process';
import { mkdirSync, renameSync, rmSync } from 'node:fs';
import { join } from 'node:path';

import { chromium } from '@playwright/test';

const BASE = process.env['BASE_URL'] ?? 'http://localhost:4200';
const SLUG = 'copa-da-vila-2026';
const SAIDA = process.argv[2] ?? 'demo/saida';
const TAMANHO = { width: 1280, height: 720 };
const senhaDaOrganizacao = process.env['DEMO_ORGANIZER_PASSWORD'];
if (!senhaDaOrganizacao) throw new Error('Defina DEMO_ORGANIZER_PASSWORD com o valor do .env.');

// Um cursor desenhado na página: o vídeo do navegador não mostra o do sistema.
function palco() {
  window.addEventListener('DOMContentLoaded', () => {
    const cursor = document.createElement('div');
    Object.assign(cursor.style, {
      position: 'fixed',
      zIndex: '2147483647',
      left: '-40px',
      top: '-40px',
      width: '20px',
      height: '20px',
      margin: '-10px 0 0 -10px',
      borderRadius: '50%',
      background: 'rgba(255, 255, 255, 0.8)',
      border: '2px solid #f97316',
      boxShadow: '0 0 0 4px rgba(249, 115, 22, 0.25)',
      pointerEvents: 'none',
      transition: 'transform 120ms',
    });
    document.body.appendChild(cursor);
    const mover = (evento) => {
      cursor.style.left = `${evento.clientX}px`;
      cursor.style.top = `${evento.clientY}px`;
    };
    document.addEventListener('mousemove', mover, true);
    document.addEventListener('mousedown', () => (cursor.style.transform = 'scale(0.7)'), true);
    document.addEventListener('mouseup', () => (cursor.style.transform = ''), true);
  });
}

const pausa = (page, ms = 1_500) => page.waitForTimeout(ms);

async function assentar(page) {
  await page.waitForLoadState('networkidle');
  await page
    .locator('app-loading')
    .first()
    .waitFor({ state: 'detached', timeout: 10_000 })
    .catch(() => {});
  await page.mouse.move(TAMANHO.width - 120, 160);
  await pausa(page, 900);
}

async function abrir(page, url) {
  await page.goto(BASE + url);
  await assentar(page);
}

async function clicar(page, alvo) {
  await alvo.scrollIntoViewIfNeeded();
  const caixa = await alvo.boundingBox();
  if (caixa) {
    await page.mouse.move(caixa.x + caixa.width / 2, caixa.y + caixa.height / 2, { steps: 25 });
    await pausa(page, 350);
  }
  await alvo.click();
}

async function ir(page, nome, url) {
  const link = page.getByRole('link', { name: nome, exact: true }).first();
  if (await link.isVisible().catch(() => false)) {
    await clicar(page, link);
    await page.waitForURL((endereco) => endereco.pathname === url, { timeout: 10_000 });
    await assentar(page);
  } else {
    await abrir(page, url);
  }
}

async function rolar(page, total, volta = true) {
  for (let feito = 0; feito < total; feito += 80) {
    await page.mouse.wheel(0, 80);
    await page.waitForTimeout(45);
  }
  await pausa(page, 1_200);
  if (volta) {
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'smooth' }));
    await pausa(page, 900);
  }
}

async function cartela(page, ato, titulo, frase) {
  await page.evaluate(
    ([ato, titulo, frase]) => {
      const tela = document.createElement('div');
      tela.id = 'video-cartela';
      tela.innerHTML = `<p>${ato}</p><h2>${titulo}</h2><span>${frase}</span>`;
      Object.assign(tela.style, {
        position: 'fixed',
        inset: '0',
        zIndex: '2147483646',
        display: 'grid',
        alignContent: 'center',
        justifyItems: 'center',
        gap: '12px',
        padding: '48px',
        textAlign: 'center',
        color: '#f8fafc',
        background: 'radial-gradient(circle at 50% 30%, #13233f, #050b14 70%)',
        fontFamily: "'Inter Variable', system-ui, sans-serif",
      });
      const [selo, cabeca, texto] = tela.children;
      Object.assign(selo.style, {
        margin: '0',
        color: '#fb923c',
        fontWeight: '700',
        letterSpacing: '0.14em',
        textTransform: 'uppercase',
      });
      Object.assign(cabeca.style, {
        margin: '0',
        fontFamily: "'Barlow Condensed', sans-serif",
        fontSize: '64px',
        fontWeight: '800',
        lineHeight: '1',
        textTransform: 'uppercase',
      });
      Object.assign(texto.style, { maxWidth: '720px', color: '#cbd5e1', fontSize: '22px' });
      document.body.appendChild(tela);
    },
    [ato, titulo, frase],
  );
  await pausa(page, 3_200);
  await page.evaluate(() => document.getElementById('video-cartela')?.remove());
  await pausa(page, 400);
}

async function entrarComoVisitante(page) {
  await page.context().clearCookies();
  await abrir(page, '/entrar');
  await clicar(page, page.getByRole('button', { name: 'Entrar como visitante' }));
  await page.waitForURL(/inicio/);
  await assentar(page);
}

mkdirSync(SAIDA, { recursive: true });
const pastaBruta = join(SAIDA, 'bruto');
const browser = await chromium.launch();
const contexto = await browser.newContext({
  viewport: TAMANHO,
  recordVideo: { dir: pastaBruta, size: TAMANHO },
  locale: 'pt-BR',
  timezoneId: 'America/Sao_Paulo',
});
await contexto.addInitScript(palco);
const page = await contexto.newPage();

const calendario = await (
  await page.request.get(`${BASE}/api/v1/public/competitions/${SLUG}/fixtures`)
).json();
const rodada = (nome) => calendario.rounds.find((item) => item.name === nome);
const goleada = rodada('Rodada 3').matches.find((jogo) => jogo.homeScore === 5);

// Ato 1 — visitante, sem conta.
await abrir(page, '/');
await cartela(
  page,
  'Ato 1 · sem conta',
  'Quem chega',
  'Campeonato, partidas, súmula e ranking abrem sem cadastro.',
);
await rolar(page, 1_400);
await ir(page, 'Ver campeonatos', '/campeonatos');
await clicar(page, page.getByRole('link', { name: /Copa da Vila/ }).first());
await page.waitForURL(new RegExp(`/c/${SLUG}$`));
await assentar(page);
await rolar(page, 700);
await ir(page, 'Partidas', `/c/${SLUG}/partidas`);
await rolar(page, 500, false);
await abrir(page, `/c/${SLUG}/partidas/${goleada.id}`);
await rolar(page, 600);
await ir(page, 'Tabela', `/c/${SLUG}/tabela`);
await pausa(page);
await ir(page, 'Ranking', `/c/${SLUG}/ranking`);
await pausa(page, 2_000);

// Ato 2 — quem joga, pela conta pública de visitante.
await cartela(
  page,
  'Ato 2 · conta de visitante',
  'Quem joga',
  'O total nunca é caixa-preta: cada ponto mostra de onde veio.',
);
await entrarComoVisitante(page);
await rolar(page, 900);
await ir(page, 'Meu time', `/c/${SLUG}/escalacao`);
await rolar(page, 500);
await ir(page, 'Mercado', `/c/${SLUG}/mercado`);
await rolar(page, 700);
await ir(page, 'Rodadas', `/c/${SLUG}/rodadas`);
await pausa(page);
await abrir(page, `/c/${SLUG}/pontuacao/${rodada('Rodada 2').id}`);
await rolar(page, 900);
await ir(page, 'Classificação', `/c/${SLUG}/ranking`);
await pausa(page, 2_000);
await clicar(page, page.getByRole('button', { name: /^Avisos/ }));
await pausa(page, 2_500);
await page.keyboard.press('Escape');

// Ato 3 — quem organiza.
await cartela(
  page,
  'Ato 3 · conta da organização',
  'Quem organiza',
  'A organização tem casa própria e lança a súmula do jeito que já faz.',
);
await page.context().clearCookies();
await abrir(page, '/entrar');
await clicar(page, page.getByLabel('E-mail'));
await page
  .getByLabel('E-mail')
  .pressSequentially('organizacao@demo.cartola-varzea.test', { delay: 25 });
await clicar(page, page.getByLabel('Senha'));
await page.getByLabel('Senha').pressSequentially(senhaDaOrganizacao, { delay: 25 });
await clicar(page, page.getByRole('button', { name: 'Entrar', exact: true }));
await page.waitForURL(/inicio/);
await assentar(page);
const [liga] = await (await page.request.get(`${BASE}/api/v1/organizations/mine`)).json();
const [copa] = await (
  await page.request.get(`${BASE}/api/v1/organizations/${liga.id}/competitions`)
).json();
const rodadas = await (
  await page.request.get(`${BASE}/api/v1/competitions/${copa.id}/rounds`)
).json();
const quarta = rodadas.find((item) => item.name === 'Rodada 4');
const segunda = rodadas.find((item) => item.name === 'Rodada 2');
await ir(page, 'Organizar', '/organizar');
await pausa(page);
await abrir(page, `/organizar/c/${copa.id}`);
await rolar(page, 500);
await ir(page, 'Rodadas', `/organizar/c/${copa.id}/rodadas`);
await rolar(page, 500);
await abrir(page, `/organizar/c/${copa.id}/partidas/${quarta.matches[0].id}/sumula`);
await pausa(page, 2_500);

// Ato 4 — apuração.
await cartela(
  page,
  'Ato 4 · apuração',
  'A rodada sai',
  'Publicar apura tudo de uma vez, numa revisão que não muda depois.',
);
await abrir(page, `/organizar/c/${copa.id}/rodadas/${quarta.id}/revisao`);
await rolar(page, 600);
await clicar(page, page.getByRole('button', { name: 'Publicar resultado' }));
await pausa(page, 2_000);
await clicar(page, page.getByRole('dialog').getByRole('button', { name: 'Publicar', exact: true }));
await page.getByRole('dialog').waitFor({ state: 'detached' });
await assentar(page);
await pausa(page, 2_000);
await abrir(page, `/organizar/c/${copa.id}/rodadas/${segunda.id}/revisao`);
await rolar(page, 600);

// Ato 5 — o ranking muda na hora.
await cartela(
  page,
  'Ato 5 · ranking',
  'A tabela anda',
  'O ranking é refeito a cada leitura: a Rodada 4 já entra na conta.',
);
await entrarComoVisitante(page);
await ir(page, 'Classificação', `/c/${SLUG}/ranking`);
await rolar(page, 600);
await cartela(
  page,
  'Cartola Várzea',
  'Fim da demonstração',
  'Projeto de portfólio, com dados fictícios. Sem apostas em dinheiro.',
);

const video = page.video();
await contexto.close();
await browser.close();
const webm = join(SAIDA, 'cartola-varzea-roteiro.webm');
renameSync(await video.path(), webm);
rmSync(pastaBruta, { recursive: true, force: true });
const mp4 = join(SAIDA, 'cartola-varzea-roteiro.mp4');
try {
  execFileSync('ffmpeg', [
    '-y',
    '-loglevel',
    'error',
    '-i',
    webm,
    '-c:v',
    'libx264',
    '-crf',
    '24',
    '-preset',
    'slow',
    '-pix_fmt',
    'yuv420p',
    '-movflags',
    '+faststart',
    mp4,
  ]);
  console.log('vídeo:', mp4);
} catch {
  console.log('vídeo:', webm, '(sem ffmpeg, sem a versão mp4)');
}
