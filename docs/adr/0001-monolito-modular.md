# ADR-001 — Monólito modular com Clean Architecture pragmática

- **Status:** aceita
- **Data:** 2026-09-14

## Contexto

O projeto é uma plataforma web de fantasy game para campeonatos amadores de futebol, desenvolvida inicialmente por uma pessoa como demonstração pública de portfólio. O domínio tem regras não triviais (mercado, escalação, pontuação versionada, valorização, correção de rodadas) e precisa isolar dados entre organizações e campeonatos.

Forças em jogo:

- operação pequena, com baixo custo cognitivo e de infraestrutura;
- limites de domínio claros, para que regras de pontuação, catálogo esportivo e identidade evoluam sem se misturar;
- apuração de rodada precisa ser transacional e síncrona do ponto de vista do organizador;
- futuras modalidades (futsal, society, campo) devem caber sem reescrever o núcleo;
- o projeto deve demonstrar boas práticas de arquitetura sem complexidade artificial.

## Decisão

Construir **um único deployable ASP.NET Core** organizado como **monólito modular**, com camadas inspiradas em Clean Architecture:

- `Domain`: entidades, value objects e regras puras, sem dependência de infraestrutura;
- `Application`: casos de uso por feature, portas, validação e autorização contextual;
- `Infrastructure`: EF Core, Identity, e-mail, armazenamento, relógio e observabilidade;
- `Api`: endpoints finos, autenticação, autorização, rate limiting e Problem Details.

Dentro dessas camadas, o código é dividido por módulos de negócio: Identity, PlatformAdministration, Organizations, Competitions, SportsCatalog, Fixtures, Statistics, Fantasy, Scoring, Leagues, Notifications, Media e Audit.

Regras de convivência:

- módulos se comunicam por interfaces da camada Application, no mesmo processo;
- eventos internos após o commit só para efeitos não críticos (notificações, projeções);
- um único banco, mas cada módulo é dono das próprias gravações;
- testes de arquitetura verificam dependências entre camadas e módulos.

## Alternativas consideradas

| Alternativa | Por que não agora |
|---|---|
| Microserviços | Custo de implantação, rede, consistência distribuída e observabilidade desproporcional para uma pessoa e uma demo |
| Monólito em camadas sem módulos | Simples no início, mas tende a misturar regras de fantasy, catálogo e identidade |
| Vertical slices sem camadas | Boa produtividade, porém dificulta isolar o domínio puro e as regras de pontuação testáveis |
| CQRS com armazenamentos separados e mensageria | Complexidade operacional sem necessidade medida |

## Consequências

Positivas:

- um deploy, um banco e transações locais para a apuração;
- fronteiras de módulo explícitas e verificáveis por teste;
- extração futura de um módulo continua possível se houver necessidade real.

Negativas e riscos:

- disciplina é necessária para não atravessar módulos por atalhos de `DbContext`;
- escala horizontal é da aplicação inteira, não por módulo;
- apuração pesada roda no mesmo processo que requisições comuns.

## Gatilhos para reavaliar

- pontuação ao vivo ou processamento que não caiba em uma requisição;
- necessidade de implantação independente por módulo;
- modalidades com regras tão divergentes que exijam núcleos separados;
- equipe crescer a ponto de módulos precisarem de ciclos de entrega próprios.

## Atualizações

- **2026-09-16:** futsal e campo entraram no MVP junto do Fut7; society continua futura. A decisão não muda: as diferenças entre modalidades ficam em parâmetros versionados (formação, orçamento, limites e pontuação), não em núcleos separados.
