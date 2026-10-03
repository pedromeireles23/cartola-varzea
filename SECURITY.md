# Política de segurança

## Versões cobertas

A versão `v1.0.0-demo` é a release final de portfólio. Correções de segurança entram na branch `main`; não há outras versões mantidas.

## Como relatar uma vulnerabilidade

**Não abra issue, pull request ou discussão pública.**

Use o relato privado do GitHub: aba **Security** → **Report a vulnerability**, ou diretamente em
<https://github.com/pedromeireles23/cartola-varzea/security/advisories/new>.

Inclua, se possível:

- componente ou tela afetada;
- passos para reproduzir;
- impacto esperado (por exemplo, acesso a dados de outro campeonato);
- versão ou commit usado.

Não envie dados pessoais de terceiros, credenciais reais ou exploits contra ambientes que você não controla.

## O que esperar

- Confirmação de recebimento em até 7 dias.
- Avaliação inicial e combinação de próximos passos em até 14 dias.
- Crédito no aviso de correção, se você desejar.

Este é um projeto de portfólio mantido por uma pessoa; os prazos são de melhor esforço.

## Escopo

Relatos especialmente relevantes:

- quebra de autenticação ou tomada de conta;
- acesso ou alteração de dados de outro usuário, organização ou campeonato;
- elevação de privilégio entre participante, auxiliar, organizador e administrador;
- escrita por conta de demonstração somente leitura;
- alteração indevida de súmula, escalação congelada, pontos, preços ou rankings;
- injeção, XSS, upload malicioso, SSRF ou exposição de segredo.

Fora do escopo: ataques de negação de serviço volumétricos, engenharia social e achados sem caminho de exploração demonstrável na aplicação.

## Testes permitidos

Teste apenas em ambiente local próprio. O projeto não está hospedado: não existe ambiente público de demonstração, e qualquer endereço que diga sê-lo não é deste repositório.
