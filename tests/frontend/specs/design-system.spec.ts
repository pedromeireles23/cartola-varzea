import { expect, test, type Locator, type Page } from '@playwright/test';

import { semViolacoes } from '../support/acessibilidade';

type ParDeContraste = {
  nome: string;
  primeiroPlano: string;
  fundo: string;
  minimo: number;
};

type MedicaoDeContraste = ParDeContraste & {
  corPrimeiroPlano: string;
  corFundo: string;
  razao: number;
};

const PARES_CENTRAIS: readonly ParDeContraste[] = [
  {
    nome: 'texto no canvas',
    primeiroPlano: '--semantic-text',
    fundo: '--semantic-canvas',
    minimo: 4.5,
  },
  {
    nome: 'ação primária',
    primeiroPlano: '--button-primary-foreground',
    fundo: '--button-primary-background',
    minimo: 4.5,
  },
  {
    nome: 'foco no canvas',
    primeiroPlano: '--semantic-focus-ring',
    fundo: '--semantic-canvas',
    minimo: 3,
  },
  {
    nome: 'foco na superfície',
    primeiroPlano: '--semantic-focus-ring',
    fundo: '--semantic-surface',
    minimo: 3,
  },
  {
    nome: 'borda de controle',
    primeiroPlano: '--semantic-border-control',
    fundo: '--semantic-surface',
    minimo: 3,
  },
  {
    nome: 'badge neutra',
    primeiroPlano: '--badge-neutral-foreground',
    fundo: '--badge-neutral-background',
    minimo: 4.5,
  },
  {
    nome: 'badge de marca',
    primeiroPlano: '--badge-brand-foreground',
    fundo: '--badge-brand-background',
    minimo: 4.5,
  },
  {
    nome: 'badge de sucesso',
    primeiroPlano: '--badge-success-foreground',
    fundo: '--badge-success-background',
    minimo: 4.5,
  },
  {
    nome: 'badge de atenção',
    primeiroPlano: '--badge-warning-foreground',
    fundo: '--badge-warning-background',
    minimo: 4.5,
  },
  {
    nome: 'badge de perigo',
    primeiroPlano: '--badge-danger-foreground',
    fundo: '--badge-danger-background',
    minimo: 4.5,
  },
  {
    nome: 'badge informativa',
    primeiroPlano: '--badge-info-foreground',
    fundo: '--badge-info-background',
    minimo: 4.5,
  },
  {
    nome: 'badge ao vivo',
    primeiroPlano: '--badge-live-foreground',
    fundo: '--badge-live-background',
    minimo: 4.5,
  },
];

async function abrirLaboratorio(page: Page): Promise<void> {
  await page.goto('/sistema/visual');
  await expect(page.getByTestId('design-system-showcase')).toBeVisible();
}

async function medirContrastes(tema: Locator): Promise<MedicaoDeContraste[]> {
  return tema.evaluate(
    (elemento, pares) => {
      type Rgba = { r: number; g: number; b: number; a: number };

      const estiloDoTema = getComputedStyle(elemento);

      function normalizarCor(token: string): string {
        const valor = estiloDoTema.getPropertyValue(token).trim();

        if (!valor) {
          throw new Error(`Token de cor ausente: ${token}`);
        }

        const prova = document.createElement('span');
        prova.style.position = 'absolute';
        prova.style.color = valor;
        prova.style.pointerEvents = 'none';
        elemento.appendChild(prova);
        const normalizada = getComputedStyle(prova).color;
        prova.remove();

        if (!normalizada) {
          throw new Error(`Cor inválida em ${token}: ${valor}`);
        }

        return normalizada;
      }

      function lerCor(valor: string): Rgba {
        const canais = valor.match(/[\d.]+/g)?.map(Number);

        if (!canais || canais.length < 3) {
          throw new Error(`Não foi possível interpretar a cor: ${valor}`);
        }

        return {
          r: canais[0],
          g: canais[1],
          b: canais[2],
          a: canais[3] ?? 1,
        };
      }

      function compor(primeiroPlano: Rgba, fundo: Rgba): Rgba {
        return {
          r: primeiroPlano.r * primeiroPlano.a + fundo.r * (1 - primeiroPlano.a),
          g: primeiroPlano.g * primeiroPlano.a + fundo.g * (1 - primeiroPlano.a),
          b: primeiroPlano.b * primeiroPlano.a + fundo.b * (1 - primeiroPlano.a),
          a: 1,
        };
      }

      function luminancia(cor: Rgba): number {
        const linearizar = (canal: number) => {
          const normalizado = canal / 255;
          return normalizado <= 0.04045
            ? normalizado / 12.92
            : Math.pow((normalizado + 0.055) / 1.055, 2.4);
        };

        return 0.2126 * linearizar(cor.r) + 0.7152 * linearizar(cor.g) + 0.0722 * linearizar(cor.b);
      }

      return pares.map((par) => {
        const corPrimeiroPlano = normalizarCor(par.primeiroPlano);
        const corFundo = normalizarCor(par.fundo);
        const fundo = lerCor(corFundo);

        if (fundo.a !== 1) {
          throw new Error(`O fundo ${par.fundo} precisa ser opaco para medir contraste.`);
        }

        const primeiroPlano = compor(lerCor(corPrimeiroPlano), fundo);
        const luminanciaPrimeiroPlano = luminancia(primeiroPlano);
        const luminanciaFundo = luminancia(fundo);
        const maisClara = Math.max(luminanciaPrimeiroPlano, luminanciaFundo);
        const maisEscura = Math.min(luminanciaPrimeiroPlano, luminanciaFundo);

        return {
          ...par,
          corPrimeiroPlano,
          corFundo,
          razao: (maisClara + 0.05) / (maisEscura + 0.05),
        };
      });
    },
    [...PARES_CENTRAIS],
  );
}

test.describe('laboratório visual V1', () => {
  test('mostra os dois temas sem violações automáticas de acessibilidade', async ({ page }) => {
    await abrirLaboratorio(page);

    await expect(page.getByTestId('theme-player')).toBeVisible();
    await expect(page.getByTestId('theme-organizer')).toBeVisible();
    await semViolacoes(page, 'laboratório visual V1');
  });

  test('carrega e aplica a fonte Barlow Condensed', async ({ page }) => {
    await abrirLaboratorio(page);

    const cabecalho = page.getByTestId('header-game').getByRole('heading', { level: 2 });
    await expect(cabecalho).toBeVisible();

    const fonte = await cabecalho.evaluate(async (elemento) => {
      const declaracao = '800 32px "Barlow Condensed"';
      const faces = await document.fonts.load(declaracao, 'Rodada decisiva');
      await document.fonts.ready;

      return {
        disponivel: faces.length > 0 && document.fonts.check(declaracao, 'Rodada decisiva'),
        familiaAplicada: getComputedStyle(elemento).fontFamily,
      };
    });

    expect(fonte.disponivel, 'A face local de Barlow Condensed não foi carregada').toBe(true);
    expect(fonte.familiaAplicada).toContain('Barlow Condensed');
  });

  test('não cria rolagem horizontal a 320 px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    await abrirLaboratorio(page);

    const estouro = await page.evaluate(() => ({
      documento: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      corpo: document.body.scrollWidth - document.body.clientWidth,
    }));

    expect(estouro.documento, 'O documento rola horizontalmente a 320 px').toBeLessThanOrEqual(0);
    expect(estouro.corpo, 'O corpo rola horizontalmente a 320 px').toBeLessThanOrEqual(0);
  });

  test('remove animações contínuas quando movimento reduzido está ativo', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await abrirLaboratorio(page);

    const animacoesContinuas = await page.getByTestId('design-system-showcase').evaluate((raiz) => {
      const elementos = [raiz, ...raiz.querySelectorAll('*')];
      const declaradas = elementos.flatMap((elemento) => {
        const estilo = getComputedStyle(elemento);
        const nomes = estilo.animationName.split(',').map((nome) => nome.trim());
        const iteracoes = estilo.animationIterationCount.split(',').map((valor) => valor.trim());

        return nomes.flatMap((nome, indice) =>
          nome !== 'none' && iteracoes[indice] === 'infinite'
            ? [`${elemento.tagName.toLowerCase()}.${elemento.className}: ${nome}`]
            : [],
        );
      });

      const executando = raiz
        .getAnimations({ subtree: true })
        .filter((animacao) => {
          const iteracoes = animacao.effect?.getTiming().iterations;
          return iteracoes === Infinity && ['pending', 'running'].includes(animacao.playState);
        })
        .map((animacao) => animacao.id || 'animação Web Animations sem nome');

      return [...declaradas, ...executando];
    });

    expect(animacoesContinuas, 'Ainda há animação infinita com prefers-reduced-motion').toEqual([]);
  });

  test('mantém contraste AA nos pares centrais dos dois temas', async ({ page }) => {
    await abrirLaboratorio(page);

    const temas = [
      ['jogador', page.getByTestId('theme-player')],
      ['organizador', page.getByTestId('theme-organizer')],
    ] as const;

    for (const [nomeDoTema, tema] of temas) {
      const medicoes = await medirContrastes(tema);

      for (const medicao of medicoes) {
        expect(
          medicao.razao,
          `${nomeDoTema} · ${medicao.nome}: ${medicao.corPrimeiroPlano} sobre ${medicao.corFundo}`,
        ).toBeGreaterThanOrEqual(medicao.minimo);
      }
    }
  });
});
