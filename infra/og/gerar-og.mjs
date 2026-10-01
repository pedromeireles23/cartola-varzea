// Gera a imagem de compartilhamento (Open Graph) a partir de cartola-varzea-og.html.
//
// Uso, na raiz do repositório, depois de instalar as dependências do frontend e do E2E:
//   node infra/og/gerar-og.mjs
//
// As fontes saem do node_modules do frontend e entram no HTML como data URL: o Chromium
// recusa @font-face carregado de file://. O Playwright é o mesmo do E2E. A saída vai para
// src/frontend/public/og/, de onde o build a publica em /og/.
import { createRequire } from 'node:module';
import { mkdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const aqui = dirname(fileURLToPath(import.meta.url));
const raiz = join(aqui, '..', '..');
const frontend = join(raiz, 'src', 'frontend');
const require = createRequire(join(raiz, 'tests', 'frontend', 'package.json'));
const { chromium } = require('@playwright/test');

function fonte(caminho) {
  const bytes = readFileSync(join(frontend, 'node_modules', caminho));
  return `data:font/woff2;base64,${bytes.toString('base64')}`;
}

const html = readFileSync(join(aqui, 'cartola-varzea-og.html'), 'utf8')
  .replace('{{BARLOW_700}}', fonte('@fontsource/barlow-condensed/files/barlow-condensed-latin-700-normal.woff2'))
  .replace('{{BARLOW_800}}', fonte('@fontsource/barlow-condensed/files/barlow-condensed-latin-800-normal.woff2'))
  .replace('{{INTER}}', fonte('@fontsource-variable/inter/files/inter-latin-wght-normal.woff2'));

const pasta = join(frontend, 'public', 'og');
mkdirSync(pasta, { recursive: true });
// JPEG: o PNG passava de 350 kB, acima do que alguns apps de mensagem aceitam bem.
const saida = join(pasta, 'cartola-varzea.jpg');

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
await page.setContent(html, { waitUntil: 'load' });
await page.evaluate(() => document.fonts.ready);
await page.locator('.cartao').screenshot({ path: saida, type: 'jpeg', quality: 90 });
await browser.close();

console.log(`${saida} (${Math.round(statSync(saida).size / 1024)} kB)`);
