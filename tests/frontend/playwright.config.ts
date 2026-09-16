import { defineConfig, devices } from '@playwright/test';

import { ADMIN_E2E_EMAIL } from './support/conta';

/**
 * Smoke E2E do corte vertical da Fase 2.
 *
 * Sobe backend e frontend por conta propria para que `npm test` funcione a partir
 * de um clone limpo. O backend precisa de SQL Server: rode o bootstrap antes
 * (docker compose + migrations), como descrito no CONTRIBUTING.
 */
const RAIZ = '../..';

export default defineConfig({
  testDir: './specs',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : [['list']],

  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
    // pt-BR e o unico idioma do MVP; o fuso do campeonato e America/Sao_Paulo.
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
  },

  projects: [
    // Garante a conta de administração uma única vez, antes dos projetos em paralelo.
    { name: 'setup', testMatch: /.*\.setup\.ts/ },
    {
      name: 'chromium-desktop',
      use: { ...devices['Desktop Chrome'] },
      dependencies: ['setup'],
    },
    // A experiencia e mobile-first: o smoke roda tambem em viewport estreita.
    { name: 'chromium-mobile', use: { ...devices['Pixel 7'] }, dependencies: ['setup'] },
  ],

  webServer: [
    {
      command: `dotnet run --project ${RAIZ}/src/backend/src/Fut7Fantasy.Api`,
      url: 'http://localhost:5277/health/ready',
      reuseExistingServer: !process.env['CI'],
      timeout: 180_000,
      // Só vale quando o próprio Playwright sobe a API (sempre no CI). Uma API local já
      // em execução usa a configuração dela, e os testes de administração avisam isso.
      env: { PlatformAdministration__InitialAdminEmail: ADMIN_E2E_EMAIL },
    },
    {
      command: `npm start --prefix ${RAIZ}/src/frontend`,
      url: 'http://localhost:4200',
      reuseExistingServer: !process.env['CI'],
      timeout: 180_000,
    },
  ],
});
