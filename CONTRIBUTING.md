# Como contribuir

Obrigado pelo interesse. O projeto está concluído como portfólio, na release `v1.0.0-demo`. Daqui em diante entram só correções críticas, de segurança e manutenção essencial — issues e pull requests nesse escopo são bem-vindos; funcionalidade nova, só se o projeto for reaberto.

## Pré-requisitos

| Ferramenta | Versão                                 | Observação                                                                             |
| ---------- | -------------------------------------- | -------------------------------------------------------------------------------------- |
| Git        | 2.40 ou superior                       |                                                                                        |
| .NET SDK   | 10.0.200 (patch mais recente da faixa) | Fixado em `global.json`                                                                |
| Docker     | Engine 24 ou superior                  | Banco, storage e e-mail locais                                                         |
| Node.js    | 24.19.0                                | Fixado em `src/frontend/.nvmrc`; o Angular 22 exige `^22.22.3 \|\| ^24.15.0 \|\| >=26` |
| npm        | 11.17.0                                | Registrado em `src/frontend/package.json` pelo campo `packageManager`                  |

## Primeiros passos

```bash
git clone https://github.com/pedromeireles23/cartola-varzea.git
cd cartola-varzea
dotnet --version     # deve mostrar 10.0.2xx
node --version       # deve mostrar v24.19.0
npm.cmd --version    # deve mostrar 11.17.0 no Windows PowerShell
dotnet tool restore  # instala o dotnet-ef fixado em .config/dotnet-tools.json
```

No Windows PowerShell com execução de scripts desabilitada, use `npm.cmd` no lugar de `npm` (por exemplo,
`npm.cmd ci`). Isso evita apenas o shim `npm.ps1`; não altera a versão instalada nem a política de segurança da
máquina. Em outros shells, os comandos `npm` abaixo funcionam normalmente.

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

| Serviço    | Porta local              | Uso                                |
| ---------- | ------------------------ | ---------------------------------- |
| SQL Server | 1433                     | Banco da aplicação                 |
| Azurite    | 10000–10002              | Blob, queue e table locais         |
| Mailpit    | 1025 (SMTP) e 8025 (web) | E-mail de conta em desenvolvimento |

Tudo escuta apenas em `127.0.0.1`. Para apagar os dados locais: `docker compose down -v`.

### Connection string

A aplicação recusa iniciar sem `Database:ConnectionString`, por decisão de projeto: configuração inválida falha no startup, não na primeira requisição. O valor nunca é versionado; em desenvolvimento fica em user-secrets.

```bash
dotnet user-secrets --project src/backend/src/Fut7Fantasy.Api set "Database:ConnectionString"   "Server=127.0.0.1,1433;Database=Fut7Fantasy;User Id=sa;Password=<senha do .env>;TrustServerCertificate=True"
```

O `bootstrap.ps1` já faz isso.

### Administrador da plataforma

Nenhum endpoint transforma uma conta em administrador. O primeiro `PlatformAdmin` vem da configuração, também em user-secrets:

```bash
dotnet user-secrets --project src/backend/src/Fut7Fantasy.Api set "PlatformAdministration:InitialAdminEmail" "voce@exemplo.local"
```

- Conta ainda não criada: cadastre-se com esse e-mail e confirme pelo link que chega no Mailpit. O papel é concedido na confirmação.
- Conta já confirmada: reinicie a API. O papel é concedido na inicialização.
- Saia e entre de novo depois da concessão: a mudança de privilégio encerra as sessões abertas.
- Tirar o e-mail da configuração não retira o papel.

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

| Rota                      | O que faz                                                       |
| ------------------------- | --------------------------------------------------------------- |
| `GET /health/live`        | Processo de pé; não consulta dependência alguma                 |
| `GET /health/ready`       | Inclui o banco; é o que indica capacidade de atender            |
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

| Pasta        | Conteúdo                                                                                    |
| ------------ | ------------------------------------------------------------------------------------------- |
| `core/`      | Cliente HTTP, configuração por ambiente, tradução de Problem Details                        |
| `layouts/`   | Casca pública e casca autenticada                                                           |
| `shared/ui/` | Design system sem regra de negócio: button, form-field, alert, card, badge, dialog, loading |
| `features/`  | Uma pasta por área funcional, carregada sob demanda                                         |

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

- Um projeto `setup` cria a conta `admin-e2e@exemplo.local` antes dos demais. A API que o próprio Playwright sobe já recebe esse e-mail como administrador inicial.
- Se uma API sua já estiver rodando, o Playwright reaproveita essa instância e a configuração dela. Nesse caso, os testes de administração são ignorados com aviso quando a conta do E2E não é administradora; no CI eles falham.
- O E2E grava contas, organizações e convites fictícios a cada execução. Para não sujar o banco de desenvolvimento, prefira rodar pelo script, que usa o banco `Fut7Fantasy_E2E` do mesmo SQL Server:

  ```powershell
  # Pare a API local antes: com a porta 5277 ocupada, o Playwright reaproveitaria ela e o banco dela.
  powershell -ExecutionPolicy Bypass -File .\infra\scripts\e2e-local.ps1
  powershell -ExecutionPolicy Bypass -File .\infra\scripts\e2e-local.ps1 -PlaywrightArgs 'specs/organization-team.spec.ts'
  ```

- Para limpar tudo, inclusive o banco de desenvolvimento: `docker compose down -v` e o `bootstrap.ps1` de novo.

### Regressão visual

Oito telas-chave — landing, Início, Meu time, Mercado, Pontuação, Classificação, Central da rodada e Súmula — são comparadas com imagens de referência em desktop e celular (`tests/frontend/visual`). Não precisam de API nem de banco: o build de produção é servido como estático, a API é respondida pelos dados gravados da demonstração em `visual/dados`, e o relógio para no instante da gravação.

As referências são geradas na imagem oficial do Playwright, a mesma do job `Regressão visual` do CI, porque a fonte renderiza diferente em cada sistema. Com o Docker no ar e o build feito (`npm run build` em `src/frontend`):

```powershell
# Conferir contra as referências
docker run --rm --ipc=host -v "${PWD}:/work" -w /work/tests/frontend mcr.microsoft.com/playwright:v1.63.0-noble npx playwright test -c visual/playwright.config.ts

# Mudança visual intencional: regerar as referências e revisar as imagens no diff
docker run --rm --ipc=host -v "${PWD}:/work" -w /work/tests/frontend mcr.microsoft.com/playwright:v1.63.0-noble npx playwright test -c visual/playwright.config.ts --update-snapshots
```

Os prints do README (`docs/imagens`) saem do mesmo harness, só com a primeira dobra de cada tela e sem comparar nada:

```powershell
docker run --rm --ipc=host -e PRINTS=/work/tmp/prints -v "${PWD}:/work" -w /work/tests/frontend mcr.microsoft.com/playwright:v1.63.0-noble npx playwright test -c visual/playwright.config.ts
```

As 16 imagens ficam em `tmp/prints`; as oito do README são copiadas de lá, com `-mobile` renomeado para `-celular`.

Quando a API mudar o formato de uma resposta usada por essas telas, regrave os dados: recrie a demo (`demo-reset.ps1`), suba a API apontando para ela, defina `DEMO_VIEWER_PASSWORD` e `DEMO_ORGANIZER_PASSWORD` com os valores do `.env` e rode `npx playwright test -c visual/playwright.config.ts` com `GRAVAR=1` em `tests/frontend`; depois, regere as referências.

### Demonstração: capturas e vídeo

O roteiro da demonstração — visitante, quem joga, quem organiza, apuração e ranking — é reproduzível por script, em `tests/frontend/demo`. Recrie a demo (`infra/scripts/demo-reset.ps1`), suba a API apontando para ela com a entrada de visitante ligada (o cabeçalho do script mostra como) e o frontend (`npm start` em `src/frontend`). Depois, em `tests/frontend`, com `DEMO_ORGANIZER_PASSWORD` definido com o valor do `.env`:

```powershell
node demo/capturas.mjs 1440   # cada passo do roteiro, página inteira, em demo/saida/capturas/1440
node demo/capturas.mjs 412    # o mesmo no celular
node demo/video.mjs           # o roteiro em vídeo, com cartelas entre os atos (demo/saida)
```

As capturas só leem. O vídeo publica a Rodada 4 de verdade, no ato da apuração: recrie a demo depois de gravar. Com o `ffmpeg` no PATH, o vídeo sai também em MP4.

O protocolo de leitor de tela do encerramento do redesign também tem script, com o TalkBack de verdade num emulador Android: `node demo/leitor-de-tela.mjs` imprime, passo a passo, o que o TalkBack falou. O cabeçalho do script traz a preparação do emulador (TalkBack e Chrome em português, registro do TalkBack em VERBOSE, `adb reverse` e `adb forward`) e pede o frontend escutando em IPv4 (`npx ng serve --host 127.0.0.1`). Toque e tecla injetados pelo `adb` não passam pelo TalkBack, então a navegação de leitura (deslizar, títulos, tabela) é simulada com foco e conferida na árvore de acessibilidade do Android. Ele só lê.

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

O `dotnet ef` grava os arquivos com BOM, CRLF e linhas longas. Os arquivos de `Persistence/Migrations` ficam fora do `editorconfig-checker` por `Exclude` no `.editorconfig-checker.json`, justamente porque o estilo é do gerador; não reescreva o arquivo à mão para satisfazer o verificador. O CI roda `dotnet ef migrations has-pending-model-changes`: mudar uma entidade sem gerar a migration correspondente quebra o build.

Antes de abrir um PR, vale rodar o mesmo verificador do CI localmente. Baixe o `editorconfig-checker` na versão fixada no `.github/workflows/ci.yml` e execute-o na raiz do repositório: ele é o único job que não tem equivalente em `dotnet format` ou `npm run lint`.

Para recriar o banco local do zero: `docker compose down -v && docker compose up -d` e aplique as migrations de novo.

## Demonstração na Azure

A demonstração publicada roda só em recursos com camada sempre gratuita: App Service F1 (Linux), Azure SQL na oferta gratuita, com pausa até o mês seguinte quando a cota acaba, e Application Insights com teto diário de ingestão. A infraestrutura está em `infra/bicep` e é aplicada pelo próprio deploy.

Preparação, uma vez, por quem é dono da assinatura, com o [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) e o `gh` logados:

```powershell
az login
powershell -ExecutionPolicy Bypass -File .\infra\scripts\azure-bootstrap.ps1 -Email voce@exemplo.com
```

O script cria o grupo de recursos, a identidade do GitHub Actions (OIDC, sem senha, válida só no ambiente `azure-demo`), as variáveis e os segredos do repositório e o orçamento com alerta por e-mail. As senhas das contas da demo na nuvem ficam também no `.env` local, como `AZURE_DEMO_*`.

Depois disso:

- **Deploy (Azure)** (`.github/workflows/deploy.yml`) roda sozinho depois de cada CI verde na `main`, ou à mão (`gh workflow run deploy.yml`): provisiona pelo Bicep, aplica as migrations por bundle, dá à identidade da Web App só leitura e escrita de dados, publica a API com o build do Angular no `wwwroot` e faz o smoke (`infra/scripts/smoke-azure.sh`).
- **Reset da demo (Azure)** (`.github/workflows/demo-reset.yml`) recria a história toda segunda; rode à mão depois do primeiro deploy, para a primeira carga.

A publicação liga três chaves que o ambiente local não usa: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, porque o TLS termina no balanceador da Azure; `Email:Enabled=false`, que fecha cadastro e recuperação de senha; e `DemoAccess:Enabled=true`. O smoke só consulta rotas que não tocam o banco, para não acordá-lo à toa e gastar a cota gratuita.

Para desligar tudo:

```powershell
az group delete --name rg-cartola-varzea
az ad app delete --id <AZURE_CLIENT_ID>   # o valor está nas variáveis do repositório
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
