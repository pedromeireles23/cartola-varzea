# ADR-009 — Importação CSV com preview e commit, sem staging persistido

- **Status:** aceita
- **Data:** 2026-09-17
- **Substitui:** a ADR-007 planejada ("CSV em staging com preview/commit")

## Contexto

O organizador precisa trazer times, atletas e técnicos de uma planilha. O fluxo desejado
sempre foi o mesmo: enviar o arquivo, ver o que aconteceria, confirmar. O desenho
planejado guardava o arquivo num staging temporário entre as duas etapas, com retenção
curta e acesso privado.

Guardar o arquivo tem custo próprio. Um arquivo enviado por terceiro, com nomes de
pessoas, passaria a existir em disco ou em blob; alguém precisaria apagá-lo no prazo,
tratar o que fazer com o que ficou órfão porque a pessoa nunca confirmou, e a
demonstração passaria a depender de Blob Storage para uma funcionalidade que não precisa
dele. A retenção curta é justamente o reconhecimento de que guardar é um risco.

Ao mesmo tempo, o motivo de existir o staging — não gravar domínio durante a
pré-visualização — não exige persistir o arquivo. Exige apenas que a pré-visualização não
escreva.

## Decisão

1. Pré-visualização e confirmação são duas requisições independentes, cada uma com o
  arquivo inteiro. Quem guarda o arquivo entre elas é o navegador de quem está
  importando.
2. O servidor lê o arquivo em memória, dentro do limite de bytes, responde, e o descarta.
  Nada é gravado em disco, em blob ou em tabela de staging. O nome do arquivo enviado
  nunca é usado como caminho nem como identificador.
3. As duas etapas percorrem exatamente o mesmo código; só o último passo difere entre
  "devolver o resumo" e "gravar". Isso torna a pré-visualização uma promessa verificável
  do que a confirmação fará, em vez de uma simulação que pode divergir.
4. A confirmação revalida tudo do zero, porque o catálogo pode ter mudado entre as duas
  etapas. Ela grava em transação única: qualquer linha inválida derruba o arquivo inteiro
  e nada é gravado.
5. A identidade que torna o reenvio idempotente é o nome, que já é único dentro do
  campeonato: nome do time, nome esportivo do atleta, e o time para o técnico. Não há um
  segundo identificador externo a manter, e é o nome que a planilha do organizador tem.
  Como consequência, renomear pelo CSV não é possível — uma linha renomeada cria outro
  registro. Renomear continua sendo ação da tela.
6. A auditoria guarda só o resumo da importação (quantos criados, alterados e sem
  mudança). O conteúdo do arquivo nunca entra em log nem em mensagem de erro.

## Consequências

- Não existe retenção a cumprir, nem limpeza de staging órfão, nem dependência de Blob
  Storage para importar. O requisito de "retenção curta" é atendido por não existir
  arquivo guardado.
- O arquivo sobe duas vezes. Para o limite de 1 MiB deste projeto, isso é irrelevante; se
  um dia o limite crescer muito, vale reconsiderar.
- Se a pessoa recarregar a página entre conferir e importar, precisa escolher o arquivo de
  novo. A tela diz isso e exige uma conferência antes de liberar a importação.
- Uma importação concorrente do mesmo campeonato é serializada pela trava na linha do
  campeonato, como no cadastro manual. Sem isso, dois arquivos simultâneos criariam o
  mesmo time duas vezes.
- O leitor de CSV é próprio, estrito e sem dependência externa. Ele reconhece apenas
  aspas, separador e quebra de linha; fórmula e macro são texto para ele. A neutralização
  de fórmula (`=`, `+`, `-`, `@`) acontece na exportação, que é onde o risco existe.
- Templates são versionados no nome do arquivo (`atletas-v1.csv`). Acrescentar coluna gera
  versão nova, e o cabeçalho é o que identifica a versão na prática: um arquivo antigo é
  recusado pela coluna que falta, com o nome dela na mensagem.
