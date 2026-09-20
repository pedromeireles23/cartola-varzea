# ADR-003 — Apuração em revisões imutáveis, com o preço atual vindo da última

- **Status:** aceita
- **Data:** 2026-09-19

## Contexto

Publicar uma rodada transforma súmulas e escalações congeladas em pontos, extrato e
preços novos. Três forças puxam o desenho:

- O resultado precisa ser explicável: todo ponto mostrado tem de apontar para a regra e
  para o evento da súmula que o gerou, mesmo depois de a súmula mudar.
- Correção é frequente. A liga apura fora da plataforma e erra com frequência; a rodada
  nasce provisória e pode ser reaberta depois de consolidada (01 §9). Uma correção não
  pode apagar em silêncio o que valia antes.
- A valorização encadeia as rodadas: a Rodada 2 parte do preço deixado pela Rodada 1.
  Se o preço atual fosse um campo sobrescrito no cadastro do atleta, reconstruir a cadeia
  depois de uma correção dependeria de desfazer atualizações.

## Decisão

1. A apuração de uma rodada é um agregado `RoundCalculation`, gravado uma vez e nunca
  alterado. Ele guarda, na mesma transação: pontos de cada atleta; o extrato de cada
  atleta por partida e item, que é a cópia dos fatos da súmula que a conta usou; o
  técnico; o total de cada participação e o resultado de cada vaga (quem jogou, quem
  contou, quem o banco cobriu); a média arredondada de cada posição; e o preço novo de
  todo ativo do catálogo, tenha jogado ou não.
2. Corrigir uma rodada gera uma nova revisão (`Revision` + 1). A revisão anterior continua
  gravada, com a versão de regra que usou. A vigente é a de revisão mais alta.
3. O preço atual de um ativo não é um campo do catálogo: é o preço novo da apuração mais
  recente do campeonato. Sem nenhuma apuração, vale o preço inicial resolvido pelo perfil
  da modalidade. Mercado, venda, patrimônio e retrato de fechamento leem daí.
4. O cálculo é uma função pura do domínio (`RoundCalculation.Compute`): súmulas,
  retratos, preços anteriores e a regra versionada entram, o resultado sai. Não lê banco
  nem relógio, e dá o mesmo resultado em qualquer ordem de entrada.
5. A publicação é idempotente pelo estado, não por chave de idempotência: publicar uma
  rodada já publicada responde sucesso sem gravar nada, e duas publicações simultâneas
  são serializadas pela trava na linha do campeonato, com um índice único em
  `(RoundId, Revision)` como última barreira. É o mesmo desenho da publicação do
  campeonato e da importação por CSV.
6. As rodadas são publicadas em ordem de fechamento do mercado: não se publica uma rodada
  enquanto outra que fechou antes ainda não foi publicada nem cancelada. A valorização de
  cada rodada parte dos preços da anterior.
7. O campeonato fica na versão de regra de pontuação da primeira rodada publicada.
  Recalibrar a modalidade não muda o jogo de quem já está jogando.

## Consequências

- O detalhamento que o participante vai ler já está gravado; a tela só consulta.
- Uma correção de súmula depois da publicação não altera a apuração vigente. Ela só vale
  quando a rodada for republicada, e então existe um antes e um depois comparáveis.
- Cada apuração grava uma linha de preço por ativo do catálogo. Com o volume de um
  campeonato de várzea (dezenas de rodadas, centenas de ativos), isso é pequeno; em
  troca, o preço atual é uma consulta a uma única apuração.
- Recalcular em cadeia depois de uma correção antiga significa gerar revisões novas das
  rodadas seguintes, cada uma partindo do preço da anterior, sem desfazer nada.
- Publicar fora de ordem não é possível. Se uma rodada ficar travada sem súmula, o
  organizador precisa resolvê-la ou cancelá-la antes de publicar as seguintes.
