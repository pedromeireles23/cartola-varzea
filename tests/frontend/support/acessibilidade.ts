import AxeBuilder from '@axe-core/playwright';
import { Page, expect } from '@playwright/test';

/**
 * Varredura automática de acessibilidade (02 §12).
 *
 * O alvo é WCAG 2.2 AA. O axe cobre o que dá para verificar por máquina — contraste,
 * nome acessível, papel, ordem de cabeçalho, marco de página —, e o 02 §12 é explícito
 * em que isso **complementa** a revisão manual, não a substitui: teclado, foco visível e
 * leitura em voz continuam sendo conferidos à mão e registrados no roadmap.
 */
export async function semViolacoes(pagina: Page, contexto: string): Promise<void> {
  // A medida é feita com movimento reduzido, e não por conveniência: sob
  // `prefers-reduced-motion` a página não anima nada, e é exatamente esse estado — o
  // conteúdo parado, inteiro e legível — que precisa passar num exame de contraste.
  // Medir no meio de uma entrada acusaria falta de contraste num texto que em repouso
  // passa com folga, e ainda deixaria de conferir se o caminho sem movimento funciona.
  await pagina.emulateMedia({ reducedMotion: 'reduce' });
  await assentar(pagina);

  const resultado = await new AxeBuilder({ page: pagina })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();

  // A falha precisa dizer o que corrigir, não só que falhou.
  const problemas = resultado.violations.map((violacao) => {
    const onde = violacao.nodes
      .slice(0, 3)
      .map((no) => no.target.join(' '))
      .join(' | ');
    return `${violacao.id} (${violacao.impact}): ${violacao.help} → ${onde}`;
  });

  expect(problemas, `Acessibilidade em ${contexto}:\n${problemas.join('\n')}`).toEqual([]);
}

/**
 * Espera as animações terminarem antes de medir.
 *
 * Sem isso o axe mede um elemento no meio da entrada, ainda em opacidade parcial, e
 * acusa contraste insuficiente num texto que em repouso passa com folga. O limite de
 * tempo existe porque animação infinita — o indicador de carregamento — nunca termina.
 */
async function assentar(pagina: Page): Promise<void> {
  await pagina.evaluate(
    () =>
      new Promise<void>((resolve) => {
        const fim = Promise.all(
          document.getAnimations().map((animacao) => animacao.finished.catch(() => undefined)),
        );
        void fim.then(() => resolve());
        setTimeout(resolve, 1_500);
      }),
  );
}
