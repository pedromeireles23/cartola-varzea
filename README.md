# Cartola Várzea

> Nome provisório. Projeto de portfólio em fase de planejamento: ainda não há código executável.

Plataforma web de fantasy game para campeonatos amadores de futebol, começando pelo Fut7. Organizadores cadastram campeonatos, times, atletas e súmulas; participantes montam equipes com atletas reais, disputam o ranking geral e criam ligas privadas.

A primeira versão será uma demonstração pública com dados fictícios, sem pagamentos, apostas ou premiações.

## Estado atual

- Regras do MVP definidas: formação, mercado, pontuação, preços e valorização (calibrados por simulação).
- Mapa de navegação e wireflows de baixa fidelidade em validação.
- Próximo passo: fundação do repositório (estrutura, CI e ambiente local).

## Stack planejada

- **Backend:** ASP.NET Core (.NET 10), Entity Framework Core, monólito modular.
- **Frontend:** Angular 22 com componentes standalone e signals, mobile-first.
- **Banco:** SQL Server local e Azure SQL Database.
- **Infraestrutura:** Azure App Service, Blob Storage, Key Vault e Application Insights, provisionados com Bicep.
- **Qualidade:** testes unitários, de integração e E2E; GitHub Actions.

A stack pode mudar durante a implementação; este README acompanha o que realmente existir.

## Licença

A definir.
