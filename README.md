# Cartola Várzea

[![CI](https://github.com/pedromeireles23/cartola-varzea/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/pedromeireles23/cartola-varzea/actions/workflows/ci.yml)
[![Licença: MIT](https://img.shields.io/badge/licen%C3%A7a-MIT-blue.svg)](LICENSE)

> Nome provisório. Projeto de portfólio em desenvolvimento, com fundações técnicas e conta local executáveis.

Plataforma web de fantasy game para campeonatos amadores de futebol, cobrindo Fut7, futsal e futebol de campo. Organizadores cadastram campeonatos, times, atletas e súmulas; participantes montam equipes com atletas reais, disputam o ranking geral e criam ligas privadas.

A primeira versão será uma demonstração pública com dados fictícios, sem pagamentos, apostas ou premiações.

## Estado atual

- Regras do MVP definidas: formação por modalidade, mercado, pontuação, preços e valorização (calibrados por simulação no Fut7).
- Mapa de navegação e wireflows de baixa fidelidade validados com um organizador de campeonato amador.
- Repositório com CI, convenções de build e decisões de arquitetura registradas ([ADR-001](docs/adr/0001-monolito-modular.md), [ADR-004](docs/adr/0004-isolamento-por-organizacao.md) e [ADR-008](docs/adr/0008-perfis-de-modalidade-versionados.md)).
- Backend: solution `Fut7Fantasy` com as camadas Api, Application, Domain e Infrastructure, EF Core com SQL Server, ASP.NET Core Identity, sessão em cookie seguro, health checks, Problem Details e testes contra SQL Server real em container.
- Ambiente local com Docker Compose (SQL Server, Azurite e Mailpit) e script de bootstrap para Windows.
- Frontend: workspace Angular 22 zoneless com tokens do design system, layouts, componentes base, página de sistema e fluxos de cadastro, confirmação, login, recuperação e perfil.
- Organizações: solicitação e aprovação de organizador, equipe auxiliar por convite (convidar, aceitar, revogar e remover), isolamento entre organizações, modo demonstração somente leitura e uma matriz de autorização verificada por teste.
- Campeonatos: criação em rascunho e configuração por organização, com modalidade (Fut7, futsal e campo) de parâmetros versionados, fuso, fechamento do mercado e prazos de resultado; fases com nome e ordem livres nos formatos grupos ou mata-mata, sem sequência esportiva obrigatória; times associados por fase e, quando aplicável, por grupo, com avanço manual; edição protegida contra sobrescrita concorrente e área própria com navegação lateral.
- Catálogo esportivo: cadastro manual de times, atletas e técnico único por time, com nomes únicos, edição concorrente segura, arquivamento/desligamento histórico, inscrição sem transferência, posição, nível ou preço exato, disponibilidade, leitura para auxiliares e fallbacks textuais — inclusive `Técnico do {time}` quando não há pessoa representante.
- Fluxos cobertos por 287 testes: 151 de backend (93 unitários, 5 de arquitetura e 53 contra SQL Server real), 103 de frontend e 33 E2E em Playwright desktop/mobile, lendo e-mails reais do Mailpit.
- Próximo passo: retomar o checklist de prontidão e a publicação do campeonato, agora que o catálogo manual mínimo e a associação de times às fases estão prontos. Google OAuth foi adiado para uma etapa posterior antes da demonstração pública.

## Stack planejada

- **Backend:** ASP.NET Core (.NET 10), Entity Framework Core, monólito modular.
- **Frontend:** Angular 22 com componentes standalone e signals, mobile-first.
- **Banco:** SQL Server local e Azure SQL Database.
- **Infraestrutura:** Azure App Service, Blob Storage, Key Vault e Application Insights, provisionados com Bicep.
- **Qualidade:** testes unitários, de integração e E2E; GitHub Actions.

A stack pode mudar durante a implementação; este README acompanha o que realmente existir.

## Licença

Distribuído sob a licença [MIT](LICENSE).
