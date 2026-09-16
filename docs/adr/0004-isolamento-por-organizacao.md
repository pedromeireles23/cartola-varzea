# ADR-004 — Isolamento por organização e autorização em camadas

- **Status:** aceita
- **Data:** 2026-09-16

## Contexto

A partir da Fase 4, uma mesma instalação passa a armazenar dados privados de
organizações diferentes. Estar autenticado ou possuir o papel global de
organizador não prova que a pessoa pode acessar uma organização específica.
Além disso, a aprovação de uma solicitação precisa criar a organização, criar
seu primeiro membro e promover a conta sem deixar estado parcial.

## Decisão

1. Todo agregado privado terá `OrganizationId` explícito. O identificador será
  resolvido e validado no servidor; valores enviados pelo cliente nunca serão
  tratados como prova de autorização.
2. Papéis globais do ASP.NET Core Identity serão usados somente para capacidades
  da plataforma (`PlatformAdmin`, `Organizer` e `DemoViewer`). Permissões dentro
  de uma organização serão representadas por `OrganizationMember`.
3. `Owner` poderá administrar a organização. Papéis auxiliares serão concedidos
  por associação e nunca por autoatribuição da própria conta.
4. Policies consultarão a associação para cada organização ou campeonato. Um
  filtro global do EF Core poderá reduzir risco acidental, mas não substituirá
  a autorização explícita nem os testes de acesso cruzado.
5. A aprovação será idempotente e executada em uma única transação no DbContext
  compartilhado com Identity: solicitação, organização, proprietário, papel
  global e auditoria mudam juntos.
6. Aprovações e rejeições registrarão ator, alvo, instante e motivo. Logs não
  substituirão o registro de auditoria persistido.
7. `DemoViewer` será bloqueado em policies de escrita no servidor, mesmo quando
  a interface não exibir comandos de edição.
8. Convites de auxiliar usarão token aleatório de 256 bits, mas somente seu hash
  será persistido. O aceite exige a conta com o e-mail convidado, cria apenas a
  associação `Assistant` e respeita expiração, revogação e idempotência.
9. Convite, aceite e revogação serão auditados sem registrar token ou e-mail. A
  autorização dessas operações será feita por policy `Owner` antes do caso de uso.
10. O primeiro `PlatformAdmin` vem da configuração
  `PlatformAdministration:InitialAdminEmail`, mantida em user-secrets ou Key Vault.
  O papel só é concedido à conta daquele e-mail depois da confirmação, de forma
  idempotente e auditada; nenhum endpoint promove contas a administrador.

## Consequências

- Consultas privadas precisam receber o escopo autorizado, aumentando um pouco
  o trabalho de cada caso de uso, mas tornando a fronteira verificável.
- O papel global `Organizer` não concede acesso a todas as organizações.
- Testes críticos devem alterar IDs deliberadamente para provar o isolamento.
- A associação permite ampliar papéis auxiliares sem misturá-los ao Identity.
- Policies de campeonato só poderão ser implementadas quando o agregado da Fase 5
  existir; até lá, a fronteira de organização é a raiz contextual disponível.
- Links de convite carregam um token opaco de ação e a interface deve consumi-lo
  rapidamente, evitando persistência, telemetria ou propagação para terceiros.
- Quem controla a configuração controla a administração da plataforma. Retirar o
  e-mail da configuração não revoga o papel; a revogação é uma ação explícita.

## Atualizações

- **2026-09-16:** a transação única da aprovação (item 5) usa read committed com
  `UPDLOCK` apenas na linha do pedido, não Serializable. O Serializable colocava
  range locks nas tabelas de papéis do Identity e aprovações simultâneas de
  pedidos diferentes entravam em deadlock. A garantia de idempotência não muda:
  aprovações do mesmo pedido continuam serializadas pelo lock da linha.
- **2026-09-16:** o token do convite é mantido só em memória pela interface
  enquanto a pessoa entra na conta; nunca vai para URL de login nem para storage.
- **2026-09-16:** a policy contextual de campeonato existe desde a Fase 5. Ela lê
  o `competitionId` da rota, busca a organização dona do campeonato no banco e
  exige a associação com ela; a organização nunca vem do cliente. Campeonato
  inexistente e campeonato de outra organização respondem igual (`403`). O
  auxiliar lê o campeonato, mas só o `Owner` cria e altera.
