# Como contribuir

Obrigado pelo interesse. O projeto está em fase inicial e é mantido como portfólio; sugestões e correções são bem-vindas por issue ou pull request.

## Pré-requisitos

| Ferramenta | Versão | Observação |
|---|---|---|
| Git | 2.40 ou superior | |
| .NET SDK | 10.0.200 (patch mais recente da faixa) | Fixado em `global.json` |
| Docker | Engine 24 ou superior | Banco, storage e e-mail locais |
| Node.js | 24.21.0 | Fixado em `src/frontend/.nvmrc`; o Angular 22 exige `^22.22.3 \|\| ^24.15.0 \|\| >=26` |

## Primeiros passos

```bash
git clone https://github.com/pedromeireles23/cartola-varzea.git
cd cartola-varzea
dotnet --version     # deve mostrar 10.0.2xx
node --version       # deve mostrar v24.21.x
dotnet tool restore  # instala o dotnet-ef fixado em .config/dotnet-tools.json
```

## Ambiente local

O `compose.yaml` sobe SQL Server, Azurite e Mailpit. As imagens são fixadas por tag e digest.

O caminho curto, no Windows PowerShell, faz tudo de uma vez: sobe os containers, espera o SQL Server ficar saudável, grava a connection string em user-secrets e aplica as migrations.

```powershell
powershell -ExecutionPolicy Bypass -File .\infra\scripts\bootstrap.ps1
```

O script é idempotente e não contém credencial: se o `.env` não existir, ele gera uma senha aleatória. Manualmente, o equivalente é:

```bash
cp .env.example .env      # defina MSSQL_SA_PASSWORD com uma senha forte local
docker compose up -d
docker compose ps         # sqlserver precisa aparecer como healthy
```

| Serviço | Porta local | Uso |
|---|---|---|
| SQL Server | 1433 | Banco da aplicação |
| Azurite | 10000–10002 | Blob, queue e table locais |
| Mailpit | 1025 (SMTP) e 8025 (web) | E-mail de conta em desenvolvimento |

Tudo escuta apenas em `127.0.0.1`. Para apagar os dados locais: `docker compose down -v`.

### Connection string

A aplicação recusa iniciar sem `Database:ConnectionString`, por decisão de projeto: configuração inválida falha no startup, não na primeira requisição. O valor nunca é versionado; em desenvolvimento fica em user-secrets.

```bash
dotnet user-secrets --project src/backend/src/Fut7Fantasy.Api set "Database:ConnectionString"   "Server=127.0.0.1,1433;Database=Fut7Fantasy;User Id=sa;Password=<senha do .env>;TrustServerCertificate=True"
```

O `bootstrap.ps1` já faz isso.

## Backend

A solution fica em `src/backend/Fut7Fantasy.slnx`; os testes em `tests/backend/`.

```bash
dotnet restore src/backend/Fut7Fantasy.slnx
dotnet build src/backend/Fut7Fantasy.slnx
dotnet test --solution src/backend/Fut7Fantasy.slnx
dotnet format src/backend/Fut7Fantasy.slnx --verify-no-changes
dotnet run --project src/backend/src/Fut7Fantasy.Api
```

Endpoints da fundação:

| Rota | O que faz |
|---|---|
| `GET /health/live` | Processo de pé; não consulta dependência alguma |
| `GET /health/ready` | Inclui o banco; é o que indica capacidade de atender |
| `GET /api/v1/system/info` | Versão, ambiente, hora do servidor e inicializações registradas |

- Os testes usam xUnit v3 no Microsoft Testing Platform (configurado em `global.json`).
- Os testes com SQL Server real sobem um container via Testcontainers. Sem Docker ligado eles são **ignorados**, não falham; no CI o Docker existe e eles executam.
- Ao adicionar ou atualizar pacote, rode `dotnet restore` e versione os `packages.lock.json` alterados; o CI restaura em modo travado.

## Frontend

O workspace Angular fica em `src/frontend`, com componentes standalone, signals e zoneless change detection.

```bash
cd src/frontend
npm ci                # instala exatamente o que está no lockfile
npm start             # dev-server em http://localhost:4200
npm test              # unitários e de componente (vitest + jsdom)
npm run lint
npm run format:check  # ou npm run format para corrigir
npm run build         # build de produção, com budgets
```

O `npm start` encaminha `/api` para `http://localhost:5277` via `proxy.conf.json`, então o código do frontend nunca conhece a porta do backend e não existe CORS em desenvolvimento. Em produção os dois são servidos na mesma origem.

Organização de `src/app`:

| Pasta | Conteúdo |
|---|---|
| `core/` | Cliente HTTP, configuração por ambiente, tradução de Problem Details |
| `layouts/` | Casca pública e casca autenticada |
| `shared/ui/` | Design system sem regra de negócio: button, form-field, alert, card, badge, dialog, loading |
| `features/` | Uma pasta por área funcional, carregada sob demanda |

Os tokens do design system ficam em `src/styles/_tokens.scss` como CSS custom properties. Componentes usam sempre o token, nunca o valor literal — é o que vai permitir acrescentar tema escuro sem reescrever cada componente.

## E2E

O smoke E2E fica em `tests/frontend`, separado do workspace Angular, e usa Playwright.

```bash
cd tests/frontend
npm ci
npm run install:browsers   # baixa o Chromium, ~115 MB, só na primeira vez
npm test
```

O Playwright sobe backend e frontend sozinho, mas o backend precisa do SQL Server: rode o `bootstrap.ps1` antes. Os testes rodam em viewport de desktop e de celular.

### Migrations

A aplicação **não** aplica migration ao iniciar. O schema é aplicado explicitamente.

Criar uma migration não exige banco no ar nem segredo: a `DesignTimeDbContextFactory` cai num placeholder quando nada está configurado.

```bash
dotnet ef migrations add NomeDaMigration --project src/backend/src/Fut7Fantasy.Infrastructure   --startup-project src/backend/src/Fut7Fantasy.Infrastructure --output-dir Persistence/Migrations
```

Aplicar, sim, exige o banco. A mesma factory lê a variável `Database__ConnectionString`, então passe a connection string pelo ambiente em vez da linha de comando — o `bootstrap.ps1` faz exatamente isso:

```powershell
$env:Database__ConnectionString = "Server=127.0.0.1,1433;Database=Fut7Fantasy;User Id=sa;Password=<senha>;TrustServerCertificate=True"
dotnet ef database update --project src/backend/src/Fut7Fantasy.Infrastructure `
  --startup-project src/backend/src/Fut7Fantasy.Infrastructure
Remove-Item Env:\Database__ConnectionString
```

O `dotnet ef` grava os arquivos com BOM e CRLF; normalize para UTF-8 sem BOM e LF antes de commitar, senão o `editorconfig-checker` reprova no CI. O CI também roda `dotnet ef migrations has-pending-model-changes`: mudar uma entidade sem gerar a migration correspondente quebra o build.

Para recriar o banco local do zero: `docker compose down -v && docker compose up -d` e aplique as migrations de novo.

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
