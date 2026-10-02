// Protocolo de leitor de tela do encerramento do redesign (V8), com o TalkBack de verdade.
//
// O Chrome de um emulador Android é dirigido pelo DevTools (Tab, Enter, Esc, cliques) e o
// TalkBack acompanha o foco; o que ele fala sai no logcat e é impresso passo a passo.
// Toque e tecla injetados pelo adb não passam pelo TalkBack — clicam direto —, então a
// navegação de leitura (deslizar, H, T) não se automatiza: para ela, o script põe o foco
// no elemento (foco simulado) e lê a árvore de acessibilidade do Android, que é o que o
// deslizar percorre. Cada leitura da árvore suspende o TalkBack por um instante.
//
// Preparação, uma vez por emulador (imagem com Google Play, TalkBack 15):
//   adb shell cmd locale set-app-locales com.android.chrome --locales pt-BR
//   adb shell cmd locale set-app-locales com.google.android.marvin.talkback --locales pt-BR
//   adb shell pm grant com.google.android.marvin.talkback android.permission.POST_NOTIFICATIONS
//   TalkBack > Configurações avançadas > Configurações de desenvolvedor > Nível de saída do
//   registro: VERBOSE (a fala aparece no logcat como `action=SPEAK text="..."`)
//   adb shell settings put secure enabled_accessibility_services \
//     com.google.android.marvin.talkback/com.google.android.marvin.talkback.TalkBackService
//
// A cada execução: demo recém-recriada, API dela no ar com a entrada de visitante ligada e
// o frontend escutando em IPv4 (`npx ng serve --host 127.0.0.1`), mais
//   adb reverse tcp:4200 tcp:4200
//   adb forward tcp:9333 localabstract:chrome_devtools_remote
// e, em tests/frontend:
//   $env:DEMO_ORGANIZER_PASSWORD = '<do .env>'; node demo/leitor-de-tela.mjs [passos, ex.: 2,7]
//
// Só lê: o diálogo de publicação é aberto e fechado com Esc, e o placar digitado na súmula
// não é salvo.
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';

import { chromium } from '@playwright/test';

const BASE = process.env['BASE_URL'] ?? 'http://localhost:4200';
const SLUG = 'copa-da-vila-2026';
const PASSOS = (process.argv[2] ?? '1,2,3,4,5,6,7,8,9').split(',').map(Number);
const senhaDaOrganizacao = process.env['DEMO_ORGANIZER_PASSWORD'];
if (!senhaDaOrganizacao) throw new Error('Defina DEMO_ORGANIZER_PASSWORD com o valor do .env.');

const SDK =
  process.env['ANDROID_HOME'] ?? join(process.env['LOCALAPPDATA'] ?? '', 'Android', 'Sdk');
const ADB = join(SDK, 'platform-tools', process.platform === 'win32' ? 'adb.exe' : 'adb');
const adb = (...args) => execFileSync(ADB, args, { encoding: 'utf8', maxBuffer: 64 << 20 });
const espera = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** Executa a ação, espera o TalkBack terminar e imprime o que ele falou. */
async function ouvir(rotulo, acao, ms = 1800) {
  adb('logcat', '-c');
  await acao();
  await espera(ms);
  const falas = adb('logcat', '-d')
    .split('\n')
    .filter((linha) => linha.includes('success=true for part=Part= speech= action=SPEAK'))
    .map((linha) => linha.match(/action=SPEAK {2}text="(.*?)" {2}options=/)?.[1])
    .filter((texto) => texto);
  console.log(`\n## ${rotulo}`);
  for (const texto of falas) console.log(`  » ${texto}`);
  if (falas.length === 0) console.log('  (silêncio)');
  return falas;
}

/** A árvore de acessibilidade do Android, na ordem em que o deslizar a percorre. */
async function arvore(rotulo, { de, ate, max = 60 } = {}) {
  adb('shell', 'uiautomator', 'dump', '/sdcard/ui.xml');
  const xml = adb('shell', 'cat', '/sdcard/ui.xml');
  const nos = [
    ...xml.matchAll(
      /<node [^>]*?text="([^"]*)"[^>]*?class="([^"]*)"[^>]*?content-desc="([^"]*)"[^>]*?enabled="([^"]*)"/g,
    ),
  ]
    .map((m) => ({
      texto: (m[1] || m[3]).replace(/&quot;/g, '"').replace(/&amp;/g, '&'),
      classe: m[2].split('.').pop(),
      desativado: m[4] === 'false',
    }))
    .filter((no) => no.texto);
  const inicio = Math.max(0, de ? nos.findIndex((no) => de.test(no.texto)) : 0);
  let fim = ate ? nos.findIndex((no, i) => i > inicio && ate.test(no.texto)) : -1;
  if (fim < 0) fim = Math.min(nos.length - 1, inicio + max);
  console.log(`\n## árvore: ${rotulo}`);
  for (const no of nos.slice(inicio, fim + 1)) {
    console.log(`  [${no.classe}${no.desativado ? ', desativado' : ''}] ${no.texto}`);
  }
  await espera(3500); // o TalkBack volta sozinho depois da leitura da árvore
}

const browser = await chromium.connectOverCDP('http://127.0.0.1:9333');
const contexto = browser.contexts()[0];
const page = contexto.pages()[0] ?? (await contexto.newPage());
await page.bringToFront();

const ir = (url, rotulo) =>
  ouvir(`carregar ${rotulo}`, () => page.goto(BASE + url, { waitUntil: 'networkidle' }), 3500);
const tecla = (rotulo, nome, ms = 1300) => ouvir(rotulo, () => page.keyboard.press(nome), ms);
const focar = (rotulo, alvo, ms = 1300) => ouvir(rotulo, () => alvo.focus(), ms);

/** Põe o foco num elemento que não o recebe, como o cursor do TalkBack ao deslizar até ele. */
const focoSimulado = (alvo, rotulo) =>
  ouvir(`foco simulado: ${rotulo}`, () =>
    alvo.evaluate((elemento) => {
      const antes = elemento.getAttribute('tabindex');
      elemento.setAttribute('tabindex', '-1');
      elemento.focus();
      elemento.addEventListener(
        'blur',
        () =>
          antes === null
            ? elemento.removeAttribute('tabindex')
            : elemento.setAttribute('tabindex', antes),
        { once: true },
      );
    }),
  );

/** Navegação por títulos (H): cada título visível, na ordem do documento. */
async function titulos(limite = 12) {
  const todos = page.locator('h1:visible, h2:visible, h3:visible');
  const total = Math.min(await todos.count(), limite);
  for (let i = 0; i < total; i++) await focoSimulado(todos.nth(i), `título ${i + 1} (H)`);
}

const calendario = await (
  await fetch(`${BASE}/api/v1/public/competitions/${SLUG}/fixtures`)
).json();
const rodada = (nome) => calendario.rounds.find((item) => item.name === nome);

if (PASSOS.includes(1)) {
  console.log('\n# PASSO 1 — Landing');
  await contexto.clearCookies();
  await ir('/', 'landing');
  await titulos();
  await page.goto(BASE + '/', { waitUntil: 'networkidle' });
  for (let i = 1; i <= 15; i++) {
    const falas = await tecla(`Tab ${i}`, 'Tab');
    if (falas.some((texto) => texto.startsWith('Entrar como visitante'))) break;
  }
  await arvore('o que vem depois da proposta (a ilustração do estádio não aparece)', {
    de: /^Créditos virtuais/,
    max: 12,
  });
}

if (PASSOS.includes(2)) {
  console.log('\n# PASSO 2 — Entrar como visitante → Início');
  if (!PASSOS.includes(1)) {
    await contexto.clearCookies();
    await ir('/', 'landing');
  }
  await ouvir(
    'clicar Entrar como visitante (o foco vai para o título)',
    async () => {
      await page.getByRole('button', { name: 'Entrar como visitante' }).first().click();
      await page.waitForURL(/inicio/);
    },
    4500,
  );
  await arvore('o prazo do mercado', { de: /^PRÓXIMA RODADA/, ate: /^Informação/ });
}

if (PASSOS.includes(3)) {
  console.log('\n# PASSO 3 — Meu time, Campo e Lista');
  await ir(`/c/${SLUG}/escalacao`, 'Meu time');
  await titulos(8);
  await focoSimulado(page.getByRole('heading', { level: 1 }).first(), 'h1, ponto de partida');
  for (let i = 1; i <= 14; i++) await tecla(`Tab ${i} (campo)`, 'Tab', 1100);
  await ouvir('alternar para Lista', () =>
    page.getByRole('button', { name: 'Lista', exact: true }).click(),
  );
  for (let i = 1; i <= 8; i++) await tecla(`Tab ${i} (lista)`, 'Tab', 1100);
  await arvore('titulares e banco em lista', { de: /^Titulares/, ate: /^Técnico:/ });
  await page.getByRole('button', { name: 'Campo', exact: true }).click();
}

if (PASSOS.includes(4)) {
  console.log('\n# PASSO 4 — Mercado, filtro por posição');
  await ir(`/c/${SLUG}/mercado`, 'Mercado');
  const posicoes = page.getByRole('group', { name: 'Posição' }).getByRole('button');
  await focar('foco no primeiro filtro', posicoes.nth(0));
  await ouvir(
    'Enter em Goleiro (a contagem é anunciada)',
    async () => {
      await posicoes.nth(1).focus();
      await page.keyboard.press('Enter');
    },
    2500,
  );
  await focar(
    'Vender, indisponível na demo',
    page.getByRole('button', { name: /^Vender/ }).first(),
  );
}

if (PASSOS.includes(5)) {
  console.log('\n# PASSO 5 — Pontuação da Rodada 2');
  await ir(`/c/${SLUG}/pontuacao/${rodada('Rodada 2').id}`, 'Pontuação da Rodada 2');
  await arvore('ordem de leitura', { de: /^SUA PONTUAÇÃO$/, max: 40 });
}

if (PASSOS.includes(6)) {
  console.log('\n# PASSO 6 — Classificação');
  await ir(`/c/${SLUG}/ranking`, 'Classificação');
  const tabela = page.getByRole('table').first();
  await focoSimulado(tabela, 'tabela (T)');
  const linhas = tabela.getByRole('row');
  const visitante = linhas.filter({ hasText: 'Você' }).first();
  for (const [rotulo, linha] of [
    ['2ª linha', linhas.nth(2)],
    ['linha do visitante', visitante],
  ]) {
    const celulas = linha.locator(':scope > th, :scope > td');
    const total = await celulas.count();
    for (let i = 0; i < total; i++)
      await focoSimulado(celulas.nth(i), `${rotulo}, célula ${i + 1}`);
  }
}

if (PASSOS.includes(7)) {
  console.log('\n# PASSO 7 — Sino de avisos');
  await ir('/inicio', 'Início');
  await focar('foco no sino', page.getByRole('button', { name: /^Avisos/ }));
  await tecla('Enter no sino', 'Enter', 2500);
  for (let i = 1; i <= 3; i++) await tecla(`Tab ${i} no painel`, 'Tab', 1200);
  await tecla('Esc (o foco volta ao sino)', 'Escape', 1800);
}

if (PASSOS.includes(8) || PASSOS.includes(9)) {
  await contexto.clearCookies();
  await page.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' });
  await page.getByLabel('E-mail').fill('organizacao@demo.cartola-varzea.test');
  await page.getByLabel('Senha').fill(senhaDaOrganizacao);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await page.waitForURL(/inicio/);
  const ids = await page.evaluate(async () => {
    const ler = (url) => fetch(`/api/v1${url}`).then((resposta) => resposta.json());
    const [liga] = await ler('/organizations/mine');
    const [copa] = await ler(`/organizations/${liga.id}/competitions`);
    const quarta = (await ler(`/competitions/${copa.id}/rounds`)).find(
      (item) => item.name === 'Rodada 4',
    );
    return { copa: copa.id, rodada: quarta.id, partida: quarta.matches[0].id };
  });

  if (PASSOS.includes(8)) {
    console.log('\n# PASSO 8 — Súmula');
    await ir(`/organizar/c/${ids.copa}/partidas/${ids.partida}/sumula`, 'súmula');
    const etapas = page.getByRole('list', { name: 'Etapas da súmula' }).getByRole('button');
    const total = await etapas.count();
    for (let i = 0; i < total; i++) await focar(`foco na etapa ${i + 1}`, etapas.nth(i), 1100);
    await tecla('Enter na etapa 5', 'Enter', 2000);
    await ouvir(
      'voltar à etapa 1',
      async () => {
        await etapas.nth(0).focus();
        await page.keyboard.press('Enter');
      },
      2000,
    );
    await focar('foco no placar do mandante', page.locator('.placar input').first(), 1500);
    await ouvir(
      'digitar 7 (o placar ao vivo é anunciado)',
      async () => {
        await page.keyboard.press('Control+A');
        await page.keyboard.type('7');
      },
      2500,
    );
  }

  if (PASSOS.includes(9)) {
    console.log('\n# PASSO 9 — Central da rodada → Publicar resultado');
    await ir(`/organizar/c/${ids.copa}/rodadas/${ids.rodada}/revisao`, 'central da rodada');
    await focar(
      'foco em Publicar resultado',
      page.getByRole('button', { name: 'Publicar resultado' }),
    );
    await tecla('Enter (o diálogo abre no Cancelar e lê o título)', 'Enter', 2500);
    await tecla('Tab no diálogo', 'Tab');
    await tecla('Shift+Tab no diálogo', 'Shift+Tab');
    await tecla('Esc (o foco volta ao botão)', 'Escape', 2000);
  }
}

// Só desconecta: fechar o browser pelo DevTools fecharia o Chrome do emulador.
process.exit(0);
