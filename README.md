# Cartola Várzea

[![CI](https://github.com/pedromeireles23/cartola-varzea/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/pedromeireles23/cartola-varzea/actions/workflows/ci.yml)
[![Licença: MIT](https://img.shields.io/badge/licen%C3%A7a-MIT-blue.svg)](LICENSE)

> Nome provisório. Projeto de portfólio em desenvolvimento, com fundações técnicas e conta local executáveis.

Plataforma web de fantasy game para campeonatos amadores de futebol, cobrindo Fut7, futsal e futebol de campo. Organizadores cadastram campeonatos, times, atletas e súmulas; participantes montam equipes com atletas reais, disputam o ranking geral e criam ligas privadas.

A primeira versão será uma demonstração pública com dados fictícios, sem pagamentos, apostas ou premiações.

## Estado atual

- Regras do MVP definidas: formação por modalidade, mercado, pontuação, preços e valorização (calibrados por simulação no Fut7).
- Mapa de navegação e wireflows de baixa fidelidade validados com um organizador de campeonato amador.
- Repositório com CI, convenções de build e decisões de arquitetura registradas ([ADR-001](docs/adr/0001-monolito-modular.md) e [ADR-004](docs/adr/0004-isolamento-por-organizacao.md)).
- Backend: solution `Fut7Fantasy` com as camadas Api, Application, Domain e Infrastructure, EF Core com SQL Server, ASP.NET Core Identity, sessão em cookie seguro, health checks, Problem Details e testes contra SQL Server real em container.
- Ambiente local com Docker Compose (SQL Server, Azurite e Mailpit) e script de bootstrap para Windows.
- Frontend: workspace Angular 22 zoneless com tokens do design system, layouts, componentes base, página de sistema e fluxos de cadastro, confirmação, login, recuperação e perfil.
- Organizações: solicitação e aprovação de organizador, equipe auxiliar por convite (convidar, aceitar, revogar e remover), isolamento entre organizações, modo demonstração somente leitura e uma matriz de autorização verificada por teste.
- Fluxos cobertos por 117 testes: 42 de backend contra SQL Server real, 52 de frontend e 23 E2E em Playwright desktop/mobile, lendo e-mails reais do Mailpit.
- Próximo passo: campeonatos, com modalidade (Fut7, futsal e campo) e configuração por organização. Google OAuth foi adiado para uma etapa posterior antes da demonstração pública.

## Stack planejada

- **Backend:** ASP.NET Core (.NET 10), Entity Framework Core, monólito modular.
- **Frontend:** Angular 22 com componentes standalone e signals, mobile-first.
- **Banco:** SQL Server local e Azure SQL Database.
- **Infraestrutura:** Azure App Service, Blob Storage, Key Vault e Application Insights, provisionados com Bicep.
- **Qualidade:** testes unitários, de integração e E2E; GitHub Actions.

A stack pode mudar durante a implementação; este README acompanha o que realmente existir.

## Licença

Distribuído sob a licença [MIT](LICENSE).
