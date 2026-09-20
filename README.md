# Cartola Várzea

[![CI](https://github.com/pedromeireles23/cartola-varzea/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/pedromeireles23/cartola-varzea/actions/workflows/ci.yml)
[![Licença: MIT](https://img.shields.io/badge/licen%C3%A7a-MIT-blue.svg)](LICENSE)

> Nome provisório. Projeto de portfólio em desenvolvimento, com fundações técnicas e conta local executáveis.

Plataforma web de fantasy game para campeonatos amadores de futebol, cobrindo Fut7, futsal e futebol de campo. Organizadores cadastram campeonatos, times, atletas e súmulas; participantes montam equipes com atletas reais, disputam o ranking geral e criam ligas privadas.

A primeira versão será uma demonstração pública com dados fictícios, sem pagamentos, apostas ou premiações.

## Estado atual

- Regras do MVP definidas: formação por modalidade, mercado, pontuação, preços e valorização (calibrados por simulação no Fut7).
- Mapa de navegação e wireflows de baixa fidelidade validados com um organizador de campeonato amador.
- Repositório com CI, convenções de build e decisões de arquitetura registradas ([ADR-001](docs/adr/0001-monolito-modular.md), [ADR-003](docs/adr/0003-apuracao-em-revisoes-imutaveis.md), [ADR-004](docs/adr/0004-isolamento-por-organizacao.md), [ADR-008](docs/adr/0008-perfis-de-modalidade-versionados.md) e [ADR-009](docs/adr/0009-importacao-csv-sem-staging.md)).
- Backend: solution `Fut7Fantasy` com as camadas Api, Application, Domain e Infrastructure, EF Core com SQL Server, ASP.NET Core Identity, sessão em cookie seguro, health checks, Problem Details e testes contra SQL Server real em container.
- Ambiente local com Docker Compose (SQL Server, Azurite e Mailpit) e script de bootstrap para Windows.
- Frontend: workspace Angular 22 zoneless com tokens do design system, layouts, componentes base, página de sistema e fluxos de cadastro, confirmação, login, recuperação e perfil.
- Organizações: solicitação e aprovação de organizador, equipe auxiliar por convite (convidar, aceitar, revogar e remover), isolamento entre organizações, modo demonstração somente leitura e uma matriz de autorização verificada por teste.
- Campeonatos: criação em rascunho e configuração por organização, com modalidade (Fut7, futsal e campo) de parâmetros versionados, fuso, fechamento do mercado e prazos de resultado; fases com nome e ordem livres nos formatos grupos ou mata-mata, sem sequência esportiva obrigatória; times associados por fase e, quando aplicável, por grupo, com avanço manual; edição protegida contra sobrescrita concorrente e área própria com navegação lateral.
- Publicação: checklist de prontidão que impede publicar um campeonato impossível de jogar (sem fase, com menos de dois times, sem atletas suficientes por posição ou sem times confirmados em nenhuma fase) e alerta sobre elenco curto, fase vazia e preços todos iguais; publicar e voltar para rascunho são do proprietário, auditados e protegidos contra decisão concorrente, e a modalidade fica travada a partir da primeira publicação.
- Rodadas e partidas: a rodada é a janela do fantasy, com as partidas apontando para a fase e para times já confirmados nela. O horário é digitado no fuso do campeonato e convertido no servidor. O fechamento do mercado é congelado na abertura e derivado do relógio dali em diante, sem job agendado; reagendar, adiar e cancelar continuam valendo com o mercado aberto, e adiar não reabre o que já fechou.
- Súmulas: organizadores e auxiliares registram placar, `didPlay`, goleiros e eventos objetivos em um editor de cinco etapas. O servidor confere gols e gols contra com o placar, gols sofridos com os goleiros, limites e motivo dos cartões, grava tudo atomicamente e protege edições concorrentes por versão.
- Revisão da rodada: organizadores e auxiliares conferem todas as partidas, placares, participantes e totais de eventos numa tela consolidada. Partida futura, súmula ausente ou fatos inconsistentes aparecem como pendências; somente o proprietário pode levar uma rodada completa a `UnderReview`, com versão lida e auditoria.
- Importação CSV: modelos versionados de times, atletas, técnicos, partidas e estatísticas da rodada, com as colunas documentadas na tela. O envio tem dois passos — conferir, que não grava nada e mostra o que aconteceria, e importar, que aplica tudo ou nada em transação única. Erros saem por linha, com o número que a planilha mostra. Reenviar o mesmo arquivo não duplica nada. Nenhum arquivo é guardado no servidor ([ADR-009](docs/adr/0009-importacao-csv-sem-staging.md)), e massa fictícia pronta fica em `infra/dados-demo`.
- Fantasy: o participante entra no campeonato publicado e recebe o orçamento da modalidade, compra no mercado agrupado por time real e escala no campo, nas três formações. A vaga vazia abre o mercado filtrado pela posição, e a compra volta para o campo. Capitão, troca com o banco e venda ficam num diálogo, sem arrastar. O servidor aplica formação, banco, orçamento e limite por time, mesmo que a tela seja ignorada, e congela a escalação completa no fechamento do mercado, pelo próprio relógio e sem job agendado.
- Apuração: a rodada em revisão é publicada de uma vez e grava a apuração em revisão imutável — pontos de cada atleta por partida e item, técnico, total e vaga de cada participação, média por posição e preço novo de todo ativo ([ADR-003](docs/adr/0003-apuracao-em-revisoes-imutaveis.md)). O cálculo é uma função pura das súmulas e da escalação congelada, com a regra de pontuação versionada por modalidade e coberta por testes de ouro. Publicar de novo não apura outra vez, as rodadas saem na ordem do fechamento do mercado, o preço atual do mercado passa a vir da última apuração e a rodada consolida sozinha pelo relógio, no fim da janela de correção.
- Face pública: busca de campeonatos publicados por nome, temporada ou organização e página do campeonato em `/c/:campeonato`, com as regras da modalidade, as fases com seus times e o catálogo. O endereço é um slug estável, gerado na primeira publicação, que sobrevive a renomear o campeonato; rascunho não aparece na busca e responde como inexistente.
- Catálogo esportivo: cadastro manual de times, atletas e técnico único por time, com nomes únicos, edição concorrente segura, arquivamento/desligamento histórico, inscrição sem transferência, posição, nível ou preço exato, disponibilidade, leitura para auxiliares e fallbacks textuais — inclusive `Técnico do {time}` quando não há pessoa representante.
- Fluxos cobertos por testes: 443 de backend (349 unitários, 5 de arquitetura e 89 contra SQL Server real), 184 de frontend e 49 E2E em Playwright desktop/mobile, lendo e-mails reais do Mailpit. Um deles espera o mercado fechar pelo relógio real e vê a escalação congelar na tela do celular.
- Próximo passo: a tela de pontuação do participante, com o detalhamento que já está gravado, e a reabertura de rodada para correção, com recálculo em cadeia. Google OAuth foi adiado para uma etapa posterior antes da demonstração pública.

## Stack planejada

- **Backend:** ASP.NET Core (.NET 10), Entity Framework Core, monólito modular.
- **Frontend:** Angular 22 com componentes standalone e signals, mobile-first.
- **Banco:** SQL Server local e Azure SQL Database.
- **Infraestrutura:** Azure App Service, Blob Storage, Key Vault e Application Insights, provisionados com Bicep.
- **Qualidade:** testes unitários, de integração e E2E; GitHub Actions.

A stack pode mudar durante a implementação; este README acompanha o que realmente existir.

## Licença

Distribuído sob a licença [MIT](LICENSE).
