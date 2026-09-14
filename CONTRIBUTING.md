# Como contribuir

Obrigado pelo interesse. O projeto está em fase inicial e é mantido como portfólio; sugestões e correções são bem-vindas por issue ou pull request.

## Pré-requisitos

| Ferramenta | Versão | Observação |
|---|---|---|
| Git | 2.40 ou superior | |
| .NET SDK | 10.0.200 (patch mais recente da faixa) | Fixado em `global.json` |

Node.js, Docker e as instruções para subir backend, frontend e banco local serão adicionados quando essas partes existirem.

## Primeiros passos

```bash
git clone https://github.com/pedromeireles23/cartola-varzea.git
cd cartola-varzea
dotnet --version   # deve mostrar 10.0.2xx
```

## Convenções

### Idioma

- Código, classes, tabelas, endpoints e nomes técnicos: **inglês**.
- Interface, documentação e mensagens de commit: **português do Brasil**.

### Código

- O `.editorconfig` define codificação (UTF-8), quebra de linha (LF), indentação e estilo de C#.
- `Directory.Build.props` trata avisos como erros e ativa análise estática: o build precisa terminar sem avisos.
- Versões de pacotes NuGet ficam centralizadas em `Directory.Packages.props` e são sempre fixas.
- Datas persistidas em UTC; cálculos de pontos e créditos usam `decimal`.

### Commits

Use [Conventional Commits](https://www.conventionalcommits.org/pt-br/) em português:

```text
feat: adiciona escalação da rodada
fix: corrige limite por time na semifinal
docs: explica importação de atletas por CSV
chore: atualiza dependências do frontend
```

Commits pequenos, com uma intenção por commit.

### Branches e pull requests

1. Crie uma branch a partir da `main`: `feat/escalacao`, `fix/limite-por-time`, `docs/csv`.
2. Abra o pull request preenchendo o template: por quê, o que mudou, como verificar e riscos.
3. O CI precisa passar antes do merge.
4. Nunca inclua segredos, credenciais ou dados pessoais reais. Toda massa de demonstração é fictícia.

## Segurança

Não abra issue pública para vulnerabilidades. Siga as instruções de [SECURITY.md](SECURITY.md).

## Licença

Ao contribuir, você concorda que sua contribuição será distribuída sob a [licença MIT](LICENSE).
