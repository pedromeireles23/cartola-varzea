import { expect, test } from '@playwright/test';

import { semViolacoes } from '../support/acessibilidade';

/**
 * Varredura de acessibilidade das telas públicas que existem sem dado nenhum (Fase 8).
 *
 * As telas que dependem de um campeonato publicado são varridas dentro da jornada que
 * já o constrói, para não montar o mundo duas vezes.
 */
const TELAS: readonly (readonly [string, string])[] = [
  ['/', 'landing'],
  ['/campeonatos', 'busca de campeonatos'],
  ['/regras', 'regras de pontuação'],
  ['/entrar', 'entrada'],
  ['/cadastro', 'cadastro'],
  ['/c/campeonato-que-nao-existe-2026', 'campeonato inexistente'],
];

for (const [endereco, nome] of TELAS) {
  test(`a tela ${nome} não tem violação automática de acessibilidade`, async ({ page }) => {
    await page.goto(endereco);
    // Sem esperar o conteúdo, o axe varreria o estado de carregamento.
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await semViolacoes(page, nome);
  });
}

test('o teclado alcança o conteúdo pela primeira parada, sem passar pelo cabeçalho', async ({
  page,
}) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  // O primeiro Tab precisa cair no atalho de pular, que é o que salva quem navega por
  // teclado de atravessar a navegação inteira em toda página (02 §12). O elemento em
  // foco é lido pelo documento, e não por um seletor `:focus`, que não acha nada quando
  // o foco ainda está no corpo da página.
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  await page.keyboard.press('Tab');
  const foco = await page.evaluate(() => document.activeElement?.textContent?.trim() ?? '');
  expect(foco).toBe('Pular para o conteúdo');

  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/#conteudo$/);
});

test('a landing responde a 200% de zoom sem rolagem horizontal', async ({ page }) => {
  // 200% de zoom numa viewport estreita é o caso do 02 §12: a tarefa principal não pode
  // quebrar. Emular metade da largura equivale a dobrar o tamanho do texto.
  await page.setViewportSize({ width: 320, height: 720 });
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  const sobra = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(sobra, 'A página rola na horizontal a 320 px').toBeLessThanOrEqual(0);
});
