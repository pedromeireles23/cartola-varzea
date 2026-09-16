# Cartola Várzea

[![CI](https://github.com/pedromeireles23/cartola-varzea/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/pedromeireles23/cartola-varzea/actions/workflows/ci.yml)
[![Licença: MIT](https://img.shields.io/badge/licen%C3%A7a-MIT-blue.svg)](LICENSE)

> Nome provisório. Projeto de portfólio em fase de planejamento: ainda não há código executável.

Plataforma web de fantasy game para campeonatos amadores de futebol, cobrindo Fut7, futsal e futebol de campo. Organizadores cadastram campeonatos, times, atletas e súmulas; participantes montam equipes com atletas reais, disputam o ranking geral e criam ligas privadas.

A primeira versão será uma demonstração pública com dados fictícios, sem pagamentos, apostas ou premiações.

## Estado atual

- Regras do MVP definidas: formação por modalidade, mercado, pontuação, preços e valorização (calibrados por simulação no Fut7).
- Mapa de navegação e wireflows de baixa fidelidade validados com um organizador de campeonato amador.
- Repositório com CI, convenções de build e primeira decisão de arquitetura ([ADR-001](docs/adr/0001-monolito-modular.md)).
- Backend: solution `Fut7Fantasy` com as camadas Api, Application, Domain e Infrastructure, EF Core com SQL Server, health checks de liveness e readiness, Problem Details e 15 testes de arquitetura e integração (dois deles contra um SQL Server real em container).
- Ambiente local com Docker Compose (SQL Server, Azurite e Mailpit) e script de bootstrap para Windows.
- Frontend: workspace Angular 22 zoneless com tokens do design system, layouts, componentes base e a primeira página consumindo a API.
- Fluxo vertical completo em pé: navegador → Angular → API → SQL Server, coberto por 31 testes (backend, frontend e E2E em Playwright).
- Próximo passo: identidade, sessão e conta.

## Stack planejada

- **Backend:** ASP.NET Core (.NET 10), Entity Framework Core, monólito modular.
- **Frontend:** Angular 22 com componentes standalone e signals, mobile-first.
- **Banco:** SQL Server local e Azure SQL Database.
- **Infraestrutura:** Azure App Service, Blob Storage, Key Vault e Application Insights, provisionados com Bicep.
- **Qualidade:** testes unitários, de integração e E2E; GitHub Actions.

A stack pode mudar durante a implementação; este README acompanha o que realmente existir.

## Licença

Distribuído sob a licença [MIT](LICENSE).
