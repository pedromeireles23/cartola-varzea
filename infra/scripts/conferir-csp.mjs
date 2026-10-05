// Confere se o index.html do build de produção roda sob a CSP que a API manda junto
// com ele (SecurityMiddleware): `script-src 'self'`, sem 'unsafe-inline'. Qualquer
// <script> sem src ou atributo de evento (onload, onclick...) seria bloqueado em
// silêncio no navegador — foi o caso do CSS crítico que o Angular embute, achado na
// Fase 17. Os testes de tela não pegam isso, porque o ng serve não manda CSP.
//
//   node infra/scripts/conferir-csp.mjs src/frontend/dist/fut7fantasy-web/browser/index.html

import { readFileSync } from 'node:fs';

const caminho = process.argv[2] ?? 'src/frontend/dist/fut7fantasy-web/browser/index.html';
const html = readFileSync(caminho, 'utf8');
const problemas = [];

for (const [tag] of html.matchAll(/<script\b[^>]*>/gi)) {
  if (!/\ssrc\s*=/i.test(tag)) {
    problemas.push(`script inline: ${tag}`);
  }
}

for (const [, atributo] of html.matchAll(/<[a-z][^>]*?\s(on[a-z]+)\s*=/gi)) {
  problemas.push(`atributo de evento: ${atributo}`);
}

if (problemas.length > 0) {
  console.error(`${caminho} não roda sob script-src 'self':`);
  for (const problema of problemas) {
    console.error(`  - ${problema}`);
  }
  process.exit(1);
}

console.log(`${caminho}: compatível com a CSP da API.`);
