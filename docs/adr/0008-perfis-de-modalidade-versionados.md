# ADR-008 — Perfis de modalidade versionados no código

- **Status:** aceita
- **Data:** 2026-09-16

## Contexto

O MVP atende três modalidades (Fut7, futsal e campo). Cada uma define formação
titular, banco, orçamento, limite de atletas por time real e preço de fallback.
Os valores foram calibrados por simulação e vão ser recalibrados quando houver
súmulas reais. Um campeonato dura semanas: se um valor mudar no meio dele, a
escalação montada com um orçamento pode ficar inválida com outro, e a apuração
deixa de ser reproduzível.

Espalhar esses números pelo código tornaria a recalibração arriscada. Guardá-los
numa tabela editável pediria uma tela de administração, abriria a porta para
mudar regra sem revisão nem teste e não resolveria o campeonato em andamento.

## Decisão

1. Os parâmetros de cada modalidade formam um `ModalityProfile` no domínio, com
  número de versão. O catálogo `ModalityProfiles` fica no código, revisado em
  pull request e coberto por testes que reproduzem as tabelas aprovadas.
2. O campeonato guarda `Modality` e `ModalityProfileVersion`. Ele nasce com a
  versão vigente e a mantém; enquanto está em rascunho, trocar a modalidade adota
  a versão vigente da nova modalidade.
3. Recalibrar significa acrescentar uma versão. Uma versão publicada nunca é
  editada, e campeonatos existentes continuam na versão com que foram criados.
4. O limite por time real é calculado a partir dos titulares e dos times ativos
  na rodada, não copiado de uma tabela literal.
5. A modalidade fica imutável depois da publicação do campeonato; o domínio
  recusa a troca e a API responde `409` com o código `competition_modality_locked`.
6. A regra de pontuação (`ScoringRuleSet`, Fase 10) seguirá o mesmo desenho:
  conjunto por modalidade, versionado no código e anexado a quem o usa.

## Consequências

- Mudar um valor exige deploy. É intencional: regra de jogo passa por teste e
  revisão, e o público vê a mesma regra que o código aplica.
- O banco guarda só a versão; ler um campeonato com uma versão que o código não
  conhece é erro de implantação e falha alto.
- A página pública de regras pode mostrar exatamente o perfil vigente, porque a
  API expõe o catálogo (`GET /api/v1/modality-profiles`).
- Remover uma versão antiga só é seguro quando nenhum campeonato aponta para ela.
