# Massa fictícia para demonstração

Catálogo pronto de um campeonato Fut7, para encher uma demonstração em minutos sem
inventar nomes na hora. **Todos os times, apelidos e técnicos são fictícios**: nenhuma
pessoa real é representada, o que é uma regra do projeto e não um detalhe (01 §11).

| Arquivo | Conteúdo |
|---|---|
| `times-v1.csv` | 6 times |
| `atletas-v1.csv` | 54 atletas: 9 por time (2 goleiros, 2 defensores, 3 meio-campistas e 2 atacantes) |
| `tecnicos-v1.csv` | 6 técnicos, um deles sem pessoa identificada, para exercitar o fallback `Técnico do {time}` |

## Como usar

Na área do campeonato, em **Importações**, envie os arquivos **nesta ordem**: times,
atletas e técnicos. Atleta e técnico apontam para o time pelo nome, então o time precisa
existir antes.

Cada arquivo é conferido antes de ser importado, e reenviar o mesmo arquivo não duplica
nada — a segunda vez mostra tudo como "sem mudança".

## Por que estes números

9 atletas por time é o mínimo que o checklist de publicação pede no Fut7 (`titulares + 2`),
então importar os três arquivos deixa o campeonato pronto para publicar assim que houver
uma fase com times confirmados. Os níveis de preço são misturados de propósito: se todos
fossem iguais, o orçamento não forçaria escolha nenhuma e o checklist alertaria sobre isso.

Os arquivos estão em UTF-8 com BOM e separador `;`, que é o que o Excel em português lê e
grava sem ajuste.
