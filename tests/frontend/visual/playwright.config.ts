import { defineConfig, devices } from '@playwright/test';

/**
 * Regressão visual de oito telas-chave (V8 do redesign).
 *
 * Roda sobre o build de produção, com a API respondida por dados gravados da
 * demonstração e o relógio parado no instante da gravação: a imagem só muda quando o
 * visual muda. As referências são geradas na imagem oficial do Playwright, a mesma do
 * job do CI, porque a renderização de fonte muda entre sistemas — o README explica como
 * atualizá-las.
 */
export default defineConfig({
  testDir: '.',
  testMatch: /telas\.spec\.ts/,
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: 0,
  workers: process.env['GRAVAR'] ? 1 : undefined,
  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : [['list']],
  snapshotPathTemplate: '{testDir}/referencias/{projectName}/{arg}{ext}',

  expect: {
    // A página inteira do Mercado é longa: com a máquina ocupada, duas capturas iguais
    // seguidas passam dos 5 s padrão.
    timeout: 20_000,
    toHaveScreenshot: {
      animations: 'disabled',
      caret: 'hide',
      scale: 'css',
      // Folga para o antisserrilhado; uma mudança de cor, espaço ou fonte passa disso.
      maxDiffPixelRatio: 0.002,
    },
  },

  use: {
    baseURL: 'http://localhost:4300',
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
    reducedMotion: 'reduce',
  },

  projects: [
    {
      name: 'desktop',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
    },
    // Pixel 7 em densidade 1: o desenho é o mesmo, e a imagem de referência fica leve.
    { name: 'mobile', use: { ...devices['Pixel 7'], deviceScaleFactor: 1 } },
  ],

  webServer: {
    command: 'node servir.mjs ../../../src/frontend/dist/fut7fantasy-web/browser 4300',
    url: 'http://localhost:4300',
    reuseExistingServer: !process.env['CI'],
    timeout: 30_000,
  },
});
