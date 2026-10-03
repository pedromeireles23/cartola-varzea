# Changelog

Mudanças relevantes do projeto. As datas são as do commit na `main`.

## [1.0.0-demo] — 2026-10-02

Primeira e única versão de portfólio: o produto está completo para demonstração, com dados fictícios, e o projeto fica concluído. Depois dela, só correções críticas, segurança e manutenção essencial.

### Segurança

- Sessão guardada no servidor: o cookie leva só a chave, e o logout apaga a sessão. Uma resposta que estava em voo durante o logout não reabre mais a sessão no navegador, e sair num navegador não derruba as outras sessões da conta.
- Dependências do frontend atualizadas para o Angular 22.2.1, sem vulnerabilidade conhecida no `npm audit`; o backend segue sem pacote vulnerável.

### Corrigido

- Sem a API no ar, a aplicação abre anônima e cada tela mostra a própria falha, em vez de ficar em branco.

### Testes

- Apuração com partida adiada (os atletas dela contam como quem não jogou, sem mexer no preço) e com time que joga duas vezes na rodada (atleta, técnico e valorização somam as duas partidas), de ponta a ponta pela API.

## 2026-09-25 a 2026-10-02 — redesign "Noite de jogo"

- Identidade de jogo esportivo em grafite e laranja: tipografia Barlow Condensed e Inter auto-hospedadas, estádio, gramado, placares e cartões de atleta em CSS e SVG próprios, sem fotografia.
- Casca lateral no desktop e navegação inferior no celular; área pública, jogo e central do organizador redesenhados; estados de carregamento, vazio e falha padronizados.
- Acessibilidade conferida com axe em 320, 412, 820 e 1440 px, teclado, foco e zoom de 200%, e o roteiro com o TalkBack de verdade.
- Regressão visual de oito telas-chave no CI, sem API nem banco.

## 2026-09-14 a 2026-09-25 — produto

- Conta local com confirmação de e-mail, recuperação de senha e sessão em cookie.
- Organizações com proprietário e auxiliares por convite, isolamento entre organizações e matriz de autorização verificada por teste.
- Campeonatos de Fut7, futsal e campo, com fases, times, atletas, técnicos e importação CSV.
- Rodadas, partidas, mercado por relógio e súmulas em cinco etapas.
- Fantasy: mercado, escalação em três formações, capitão e banco, congelados no fechamento.
- Apuração em revisões imutáveis, valorização pela média da posição, correção com recálculo em cadeia e aviso interno.
- Ranking geral e ligas privadas por código.
- Área pública do campeonato e demonstração recriada do zero, com entrada de visitante somente leitura.
