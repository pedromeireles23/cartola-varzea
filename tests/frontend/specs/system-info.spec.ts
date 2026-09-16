import { expect, test } from "@playwright/test";

/**
 * Prova o caminho completo: navegador -> Angular -> proxy -> API -> SQL Server.
 * Se qualquer elo cair, este teste falha.
 */
test("a pagina de sistema mostra dados vindos do banco", async ({ page }) => {
  await page.goto("/sistema");

  await expect(
    page.getByRole("heading", { name: "Estado do sistema" }),
  ).toBeVisible();

  const cartao = page.getByRole("region", { name: "Informação técnica" });
  await expect(cartao).toBeVisible();

  // startupCount so tem valor porque a linha foi gravada no SQL Server ao subir.
  const inicializacoes = cartao
    .getByRole("term")
    .filter({ hasText: "Inicializações registradas" });
  await expect(inicializacoes).toBeVisible();

  const valor = cartao.locator("dd").nth(3);
  await expect(valor).toHaveText(/^[1-9]\d*$/);

  await expect(cartao.getByText("UTC").first()).toBeVisible();
});

test("a navegacao principal funciona pelo teclado", async ({ page }) => {
  await page.goto("/sistema");
  // Espera a pagina renderizar: pressionar Tab antes disso testa um DOM que ainda vai mudar.
  await expect(
    page.getByRole("heading", { name: "Estado do sistema" }),
  ).toBeVisible();

  // Primeiro Tab deve alcancar o link de pular conteudo (02 §12).
  await page.keyboard.press("Tab");
  await expect(
    page.getByRole("link", { name: "Pular para o conteúdo" }),
  ).toBeFocused();

  await page.keyboard.press("Tab");
  await expect(
    page.getByRole("link", { name: "Cartola Várzea" }),
  ).toBeFocused();
});

test("nao ha rolagem horizontal em viewport estreita", async ({ page }) => {
  await page.goto("/sistema");
  await expect(
    page.getByRole("heading", { name: "Estado do sistema" }),
  ).toBeVisible();

  const estouro = await page.evaluate(
    () =>
      document.documentElement.scrollWidth >
      document.documentElement.clientWidth,
  );

  expect(estouro).toBe(false);
});
