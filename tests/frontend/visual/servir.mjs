import http from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';

/**
 * Serve o build de produção para a regressão visual.
 *
 * Na reprodução, o Playwright responde a API com os dados gravados e nada chega aqui.
 * Na gravação, `/api` segue para a API local, na porta 5277, como a produção serviria.
 */
const RAIZ = process.argv[2];
const PORTA = Number(process.argv[3] ?? 4300);
const TIPOS = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript',
  '.css': 'text/css',
  '.woff2': 'font/woff2',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.jpg': 'image/jpeg',
  '.png': 'image/png',
  '.json': 'application/json',
  '.txt': 'text/plain',
  '.webmanifest': 'application/manifest+json',
};

http
  .createServer(async (req, res) => {
    if (req.url.startsWith('/api/')) {
      const proxy = http.request(
        {
          host: 'localhost',
          port: 5277,
          path: req.url,
          method: req.method,
          headers: { ...req.headers, host: 'localhost:5277' },
        },
        (resposta) => {
          res.writeHead(resposta.statusCode, resposta.headers);
          resposta.pipe(res);
        },
      );
      proxy.on('error', () => {
        res.writeHead(502);
        res.end();
      });
      req.pipe(proxy);
      return;
    }

    let caminho = normalize(decodeURIComponent(req.url.split('?')[0]));
    while (caminho.startsWith('/') || caminho.startsWith('\\')) caminho = caminho.slice(1);
    let arquivo = join(RAIZ, caminho);
    try {
      if (!(await stat(arquivo)).isFile()) throw new Error('pasta');
    } catch {
      arquivo = join(RAIZ, 'index.html');
    }
    res.writeHead(200, { 'content-type': TIPOS[extname(arquivo)] ?? 'application/octet-stream' });
    res.end(await readFile(arquivo));
  })
  .listen(PORTA, () => console.log(`servindo ${RAIZ} em ${PORTA}`));
