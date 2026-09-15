# Sistema de Oficina Mecânica

Back-end de um sistema integrado de atendimento e execução de serviços para oficina mecânica,
desenvolvido como **Tech Challenge da pós-graduação em Arquitetura de Software (FIAP/SOAT)**.

- **Fase 1** — back-end monolítico com DDD, APIs REST, autenticação JWT e testes.
- **Fase 2** — **evolução da aplicação** (refatoração para **Clean Code + Clean Architecture** e
  novas APIs de Ordem de Serviço) **e infraestrutura** (Docker, **Kubernetes**, **Terraform**,
  **CI/CD** e escalabilidade automática com HPA).

> 📚 **Toda a documentação detalhada está em [`docs/`](docs/README.md)** — este README concentra
> o contexto e os links. Comece por [`docs/README.md`](docs/README.md) para navegar.

---

## Índice

- [Sobre o Projeto](#sobre-o-projeto)
- [Arquitetura e Desenhos da Solução](#arquitetura-e-desenhos-da-solução)
- [Execução Local (Docker Compose)](#execução-local-docker-compose)
- [Deploy na Nuvem (AWS)](#deploy-na-nuvem-aws)
- [CI/CD](#cicd)
- [Autenticação](#autenticação)
- [Observabilidade (OpenTelemetry)](#observabilidade-opentelemetry)
- [APIs — Documentação e Collection](#apis--documentação-e-collection)
- [Testes](#testes)
- [Vídeo Demonstrativo](#vídeo-demonstrativo)
- [Documentação Completa](#documentação-completa)

---

## Sobre o Projeto

### O Desafio

Construir um back-end de gestão de oficina com **DDD aplicado**, **APIs RESTful documentadas**,
**autenticação JWT**, validação de CPF/CNPJ e placa, **cobertura de testes ≥ 80%** nos domínios
críticos e orquestração via Docker (Fase 1) — e, na Fase 2, provisionar e implantar toda a
solução em **Kubernetes com IaC e CI/CD**.

### Objetivos da Fase 2

A Fase 2 evolui a Fase 1 em **dois pilares** — qualidade/organização do código **e**
infraestrutura escalável e automatizada.

**Pilar 1 — Evolução da aplicação**

| Objetivo | Entregue |
|---|---|
| **Clean Code + Clean Architecture** | Refatoração da Fase 1: separação de camadas e dependências com artefatos nomeados (Controller CA, Gateway, Presenter, Use Cases). Planos em [`docs/planos/refatoracao-clean-architecture/`](docs/planos/refatoracao-clean-architecture/) |
| **Novas/alteradas APIs de OS** | Abertura completa da OS (cliente, veículo, serviços e peças de uma vez), consulta de status, aprovação/recusa de orçamento, **listagem ordenada por status** com **exclusão lógica** de OS finalizadas/entregues, e notificação de status |
| **Testes automatizados** | Unitários (xUnit) + integração (Testcontainers) cobrindo os fluxos críticos, cobertura ≥ 80% nos domínios |

**Pilar 2 — Infraestrutura e automação**

| Objetivo | Entregue |
|---|---|
| **Conteinerização** | `Dockerfile` multi-stage + `docker-compose` para dev local |
| **Orquestração** em Kubernetes | Deployments, Services, ConfigMap, Secret e **HPA** em [`k8s/`](k8s/) |
| **Escalabilidade automática** | HorizontalPodAutoscaler por CPU (min 1 / max 5) |
| **Infraestrutura como Código** | Terraform em três repositórios dedicados — cluster/rede/ECR ([`oficina-mecanica-infra-k8s`](https://github.com/gabrielMauad/oficina-mecanica-infra-k8s)), banco ([`oficina-mecanica-infra-db`](https://github.com/gabrielMauad/oficina-mecanica-infra-db)) e a Function ([`oficina-mecanica-lambda-auth`](https://github.com/gabrielMauad/oficina-mecanica-lambda-auth)) |
| **CI/CD** | Pipeline GitHub Actions: build → teste → imagem (ECR) → deploy no EKS → smoke test |

> Detalhes das decisões de Clean Architecture em
> [`docs/arquitetura/clean-architecture.md`](docs/arquitetura/clean-architecture.md) e das mudanças
> funcionais da OS em [`docs/arquitetura/decisoes.md`](docs/arquitetura/decisoes.md).

### Objetivos Adicionais (estudo de arquitetura)

Além do MVP exigido, o projeto incorpora deliberadamente práticas avançadas:

- **Modular Monolith** com fronteiras de Bounded Context físicas (assembly por camada por
  módulo), preparando a extração futura para microsserviços com mínimo retrabalho.
- **DDD real**: domain events existem apenas quando há consumidor concreto.
- **CQRS com MediatR v12**: vertical slices, pipeline behaviors (validação, logging, transação).
- **Integration Events in-process**: bus desacoplado (`IIntegrationEventBus`) substituível por
  RabbitMQ/Kafka sem alterar Application ou Domain.
- **Anti-Corruption Layer (ACL)** para comunicação síncrona entre módulos via ports e adapters.

---

## Arquitetura e Desenhos da Solução

Os três desenhos exigidos na Fase 2 estão em [`docs/arquitetura/diagramas/`](docs/arquitetura/diagramas/)
(Mermaid — renderizam direto no GitHub):

| Desenho | Arquivo |
|---|---|
| 🧩 **Componentes da aplicação** (C4 níveis 1–3) | [`diagramas/componentes.md`](docs/arquitetura/diagramas/componentes.md) |
| 🏗️ **Infraestrutura provisionada** (EKS, RDS, API Gateway, HPA) | [`diagramas/infraestrutura.md`](docs/arquitetura/diagramas/infraestrutura.md) |
| 🚀 **Fluxo de deploy** (CI/CD) | [`diagramas/fluxo-deploy.md`](docs/arquitetura/diagramas/fluxo-deploy.md) |

### Modular Monolith + Clean Architecture

O projeto é aderente à **Clean Architecture** (os quatro anéis são projetos físicos distintos e
a Regra de Dependência é forçada em compile-time) e organizado como **Modular Monolith**: cada
Bounded Context tem 5 projetos próprios, com a fronteira enforçada por referências de projeto.
Para extrair um microsserviço, basta mover os projetos do módulo, trocar adapters por HTTP
clients e o bus in-process por mensageria — nada no Domain ou Application muda.

### Bounded Contexts

| Módulo | Responsabilidade |
|---|---|
| **Autenticacao** | Login e emissão de JWT |
| **Cadastro** | Cliente, Veículo, Serviço (catálogo) |
| **PecasInsumos** | Estoque de peças, disponibilidade, entradas e saídas |
| **OrdemServico** | Ciclo de vida completo da OS e orçamento |

### Banco de Dados

**PostgreSQL 16**, banco único (`oficina_mecanica`), **1 schema por módulo** (`cadastro`,
`pecas_insumos`, `ordem_servico`), **sem FK cross-schema** — o isolamento simula microsserviços.

> 📖 Aprofundamento: [`estrutura-do-projeto.md`](docs/arquitetura/estrutura-do-projeto.md) ·
> [`clean-architecture.md`](docs/arquitetura/clean-architecture.md) ·
> [`decisoes.md`](docs/arquitetura/decisoes.md) ·
> [`database-schema.md`](docs/arquitetura/database-schema.md) ·
> [`event-storming.md`](docs/arquitetura/event-storming.md).

---

## Execução Local (Docker Compose)

Forma mais rápida de subir tudo para desenvolvimento e testes manuais.

**Pré-requisitos:** Docker + Docker Compose v2.x. (.NET SDK 10.0 apenas para rodar testes fora do
container.)

```bash
git clone https://github.com/gabrielMauad/oficina-mecanica-app.git
cd oficina-mecanica-app

docker compose up --build
```

Sobe três serviços:
- **`postgres`** — PostgreSQL 16, banco `oficina_mecanica`, porta `5432`
- **`api`** — aplicação .NET 10, porta `8080`
- **`jaeger`** — coletor OTLP + UI para visualizar traces localmente, sem depender de uma
  ferramenta paga (ver [Observabilidade](#observabilidade-opentelemetry))

As **migrations são aplicadas automaticamente** na inicialização da API (`MigrateAsync()` em
`Program.cs`). Nenhum comando manual é necessário.

### Pontos de acesso

| Serviço | URL |
|---|---|
| API REST | `http://localhost:8080/api/v1/` |
| Scalar (docs interativa) | `http://localhost:8080/scalar` |
| Health checks | `http://localhost:8080/healthz` (agregado), `/healthz/live`, `/healthz/ready` |
| Jaeger UI (traces) | `http://localhost:16686` |
| PostgreSQL | `localhost:5432` — user/pass: `oficina` / `oficina-dev-pass` |

---

## Deploy na Nuvem (AWS)

A partir da Fase 3, a aplicação roda em **nuvem** (AWS Academy Learner Lab) — ver
[RFC-002](docs/arquitetura/rfcs/002-escolha-do-provedor-de-nuvem.md) para a escolha do provedor e
as restrições reais da conta, e
[ADR-005](docs/arquitetura/adrs/005-quatro-repositorios-e-estrategia-de-branches.md) para a
divisão em quatro repositórios. O cluster **kind** local da Fase 2 (pasta `infra/`, Terraform do
cluster efêmero criado dentro do runner) foi **removido** deste repositório — a infraestrutura
agora é persistente e gerenciada pelos repositórios de infra.

### Os quatro repositórios e a ordem de deploy

O merge de cada repositório de infraestrutura dispara `apply` automático — a ordem importa porque
há dependência real entre eles (outputs consumidos via `terraform_remote_state`, ECR/segredo
consumidos pela pipeline deste repositório, tabelas consumidas pela Lambda):

| Ordem | Repositório | Provisiona |
|---|---|---|
| 1 | [`oficina-mecanica-infra-k8s`](https://github.com/gabrielMauad/oficina-mecanica-infra-k8s) | Cluster **EKS**, NLB, **API Gateway**, **ECR**, segredo da aplicação |
| 2 | [`oficina-mecanica-infra-db`](https://github.com/gabrielMauad/oficina-mecanica-infra-db) | **RDS PostgreSQL**, segredo do banco |
| 3 | **Este repositório** (`oficina-mecanica-app`) | Imagem da API (publicada no ECR) + manifestos deste `k8s/` no cluster |
| 4 | [`oficina-mecanica-lambda-auth`](https://github.com/gabrielMauad/oficina-mecanica-lambda-auth) | Function de autenticação por CPF + rota no API Gateway |

Este repositório precisa do ECR e do segredo criados por `infra-k8s` antes de poder publicar a
imagem e criar o `Secret` do Kubernetes; a Lambda precisa das tabelas criadas pelas **migrations
desta aplicação** para o fluxo completo de login por CPF funcionar de ponta a ponta.

### Manifestos (`k8s/`)

- **`base/`** — namespace e `ConfigMap` (variáveis não sensíveis). O `Secret` (`oficina-secrets`)
  **não é commitado**: é criado pela pipeline (`ci-cd.yml`) a partir dos segredos do AWS Secrets
  Manager, em tempo de deploy — nunca em texto plano no repositório.
- **`app/`** — `Deployment` (imagem publicada no ECR), `Service` **NodePort** (porta 30080, alvo
  fixo da NLB interna provisionada por `oficina-mecanica-infra-k8s` — não altere o tipo nem a
  porta) e **HPA** (1 a 5 réplicas, 50% CPU).

Não existe mais `k8s/database/`: o PostgreSQL em pod da Fase 2 foi substituído pelo RDS gerenciado
(`oficina-mecanica-infra-db`); por isso o `Deployment` da API também não tem mais o initContainer
`wait-for-postgres` (esperava um Service que deixou de existir).

### Descobrir a URL pública da API

Nunca fica hardcoded no código ou na documentação — o endpoint muda a cada recriação da
infraestrutura:

```bash
aws apigatewayv2 get-apis \
  --query "Items[?Name=='oficina-mecanica-api'].ApiEndpoint | [0]" \
  --output text
```

`<endpoint>/scalar` é a documentação interativa publicada; `<endpoint>/healthz/ready` é o alvo do
smoke test da pipeline; `<endpoint>/auth/cpf` é a rota de autenticação por CPF (Function
Serverless, repositório `oficina-mecanica-lambda-auth`).

### Recuperar a senha do admin da oficina

Gerada pelo Terraform de `oficina-mecanica-infra-k8s` (`random_password`) e nunca commitada:

```bash
aws secretsmanager get-secret-value \
  --secret-id oficina-mecanica/dev/app \
  --query SecretString --output text | jq -r .admin_senha
```

### Limitação do deploy automático (ADR-006)

A conta AWS Academy Learner Lab não permite criar IAM role nem provedor OIDC — as pipelines dos
quatro repositórios autenticam com **credenciais de sessão temporárias**
(`AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`/`AWS_SESSION_TOKEN`), renovadas manualmente a cada
sessão do laboratório (ver
[ADR-006](docs/arquitetura/adrs/006-credenciais-de-nuvem-no-cicd.md)). **Se o job `deploy` falhar
na autenticação, não é pipeline quebrada** — é a sessão expirada; renove os três secrets do
repositório e reexecute via `workflow_dispatch`, sem precisar de um commit novo.

> Desenho da infraestrutura completa: [`diagramas/infraestrutura.md`](docs/arquitetura/diagramas/infraestrutura.md).

---

## CI/CD

Dois workflows do GitHub Actions:

| Workflow | Gatilho | O que faz |
|---|---|---|
| [`ci.yml`](.github/workflows/ci.yml) | Pull Request → `main` | build + testes (validação de PR) |
| [`ci-cd.yml`](.github/workflows/ci-cd.yml) | Push/merge → `main`, `workflow_dispatch` | build → teste → imagem (**Amazon ECR**) → deploy no cluster **EKS** (`oficina-mecanica`) → smoke test em `/healthz/ready` via **API Gateway** |

Credenciais: `aws-actions/configure-aws-credentials`, com as **três credenciais de sessão
temporárias** da conta AWS Academy (`AWS_SESSION_TOKEN` incluído — ver
[ADR-006](docs/arquitetura/adrs/006-credenciais-de-nuvem-no-cicd.md)). `workflow_dispatch` permite
reexecutar o deploy depois de renovar as credenciais, sem precisar de um commit novo. O
`Secret` do Kubernetes (`oficina-secrets`) é criado pela própria pipeline a partir dos segredos do
Secrets Manager — nenhum valor sensível é impresso no log (`::add-mask::` em cada um).

> Desenho do pipeline: [`diagramas/fluxo-deploy.md`](docs/arquitetura/diagramas/fluxo-deploy.md).

---

## Autenticação e autorização

Praticamente todos os endpoints exigem JWT, e a autorização passou a considerar o **papel** do
token — ver [RFC-001](docs/arquitetura/rfcs/001-estrategia-de-autenticacao.md) e
[ADR-003](docs/arquitetura/adrs/003-dois-emissores-e-autorizacao-por-papel.md).

### Os dois papéis

| Papel | Quem é | Emissor do token | Credencial |
|---|---|---|---|
| `Oficina` | Operador da oficina | **Esta aplicação** — `POST /api/v1/auth/login` | email + senha |
| `Cliente` | Cliente da oficina | **Function Serverless** de autenticação (repositório separado) | CPF |

**Token da oficina** — emitido pela própria aplicação:

```bash
curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email": "admin@oficina.com", "senha": "admin123"}'
```

**Token do cliente** — emitido pela Function Serverless em repositório próprio
([`oficina-mecanica-lambda-auth`](https://github.com/gabrielMauad/oficina-mecanica-lambda-auth),
ver [ADR-005](docs/arquitetura/adrs/005-quatro-repositorios-e-estrategia-de-branches.md)), na
nuvem via `POST <url-da-api>/auth/cpf` — não existe rota nesta aplicação que emita token de
cliente. Localmente (`docker compose`, sem a Lambda rodando), esse token só é obtido manualmente:
assinando um JWT em HS256 com o mesmo `Jwt__Secret` da aplicação, com
`iss=oficina-mecanica-auth`, `aud=oficina-mecanica-api`, `sub=<id do cliente>` e `role=Cliente`. É
exatamente o que o helper `CreateClienteAuthenticatedClient(clienteId)` faz nos testes de
integração
([`OficinaMecanicaWebApplicationFactory`](tests/IntegrationTests/Infrastructure/OficinaMecanicaWebApplicationFactory.cs)).

Ambos os tokens têm validade de **1 hora** e são enviados como `Authorization: Bearer <token>`
(ou pelo botão **Authorize** no Scalar). O contrato de claims está no
[RFC-001 §4.1](docs/arquitetura/rfcs/001-estrategia-de-autenticacao.md).

### Mapa de rotas por papel

| Rota | Papel exigido |
|---|---|
| `POST /api/v1/auth/login` | **Anônima** |
| `GET /healthz`, `/healthz/live`, `/healthz/ready` | **Anônima** |
| `GET /api/v1/ordens-servico/acompanhamento` | `Cliente` (lista só as OS do cliente do token) |
| `PATCH /api/v1/ordens-servico/{id}/aprovar-orcamento` | `Cliente` |
| `PATCH /api/v1/ordens-servico/{id}/rejeitar-orcamento` | `Cliente` |
| `GET /api/v1/ordens-servico/{id}` e `/{id}/status` | `Cliente` **ou** `Oficina` |
| `GET /api/v1/ordens-servico?clienteId=...` | `Oficina` |
| Demais rotas de `ordens-servico` (abertura, diagnóstico, execução, finalização, conclusão) | `Oficina` |
| Todas as rotas de `clientes`, `veiculos`, `servicos` e `pecas-insumos` | `Oficina` |

Regra adicional: um token de papel `Cliente` só opera sobre ordens de serviço **do próprio
cliente** — o `sub` do token é comparado com o dono da OS. Um cliente acessando a OS de outro
recebe **403 Forbidden**, não 404.

> **Mudança em relação à Fase 2:** `GET /api/v1/ordens-servico?clienteId=...` **deixou de ser
> anônima** e passou a exigir papel `Oficina` — uma listagem completa de ordens de serviço é
> informação sensível. O acompanhamento pelo cliente também deixou de ser público: agora é
> `GET /api/v1/ordens-servico/acompanhamento`, autenticado e filtrado pelo token.
>
> A documentação OpenAPI/Scalar continua acessível **sem autenticação** — decisão consciente,
> registrada no [RFC-001 §8](docs/arquitetura/rfcs/001-estrategia-de-autenticacao.md).

> Credenciais e segredo JWT são definidos no `docker-compose.yml` (dev) e no `Secret`
> `oficina-secrets` do Kubernetes (nuvem — criado pela pipeline a partir do secret
> `oficina-mecanica/dev/app` do Secrets Manager, ver [Deploy na Nuvem (AWS)](#deploy-na-nuvem-aws)).
> O emissor e a audience aceitos vêm de `Jwt__ValidIssuers__0` / `__1` e `Jwt__Audience`.

---

## Health checks

| Endpoint | O que verifica | Uso |
|---|---|---|
| `GET /healthz/live` | **Liveness** — só se o processo responde; não toca em dependências | `livenessProbe` do Kubernetes |
| `GET /healthz/ready` | **Readiness** — inclui o check do **PostgreSQL**; devolve `503` se o banco estiver indisponível | `readinessProbe` do Kubernetes |
| `GET /healthz` | **Agregado** — todos os checks registrados | Smoke test da pipeline (`ci-cd.yml`) |

Os três são **anônimos**. `/healthz/live` continua respondendo `200` mesmo com o banco fora do
ar — é o que separa "o processo travou" (reiniciar) de "a dependência caiu" (tirar do
balanceamento), comportamento coberto por teste de integração em
[`tests/IntegrationTests/HealthChecks`](tests/IntegrationTests/HealthChecks).

---

## Observabilidade (OpenTelemetry)

Traces e métricas via **OpenTelemetry .NET**, exportados por **OTLP** — ver
[ADR-004](docs/arquitetura/adrs/004-correlacao-via-traceid-w3c.md) para a decisão completa de
correlação. Tudo isolado em
[`src/Bootstrap/Api/Extensions/ObservabilityExtensions.cs`](src/Bootstrap/Api/Extensions/ObservabilityExtensions.cs).

**O que é instrumentado:**
- **ASP.NET Core** — requisições de entrada (latência das APIs).
- **HttpClient** — chamadas HTTP de saída.
- **Npgsql** — comandos SQL executados via EF Core/Npgsql, aparecem como spans filhos do span da
  requisição (tempo gasto em banco).
- **Runtime** (`OpenTelemetry.Instrumentation.Runtime`) — CPU, memória e GC do processo, para
  correlacionar com o consumo visto pelo `kubectl top pods` / HPA.
- Os **meters de negócio** da aplicação (`OficinaMecanica.OrdensServico`,
  `OficinaMecanica.Integracoes`) já estão registrados no provedor de métricas — as métricas em si
  são publicadas por outra frente de trabalho.

**Amostragem em 100%** (`AlwaysOnSampler`), decisão da ADR-004: nenhum trace usado como evidência
para o vídeo de entrega pode ser descartado. Isso é deliberado para o volume deste projeto — **não
seria adequado em produção real**, onde amostragem parcial é necessária.

**Destino configurável, sem acoplamento a fornecedor.** O SDK usa as variáveis de ambiente padrão
do OpenTelemetry — não há nenhum SDK/agente da New Relic ou Datadog no código:

| Variável | Efeito |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Endpoint OTLP (gRPC) de destino — coletor local, New Relic, Datadog Agent, etc. |
| `OTEL_EXPORTER_OTLP_HEADERS` | Headers extras (ex.: chave de API do APM), no formato `chave1=valor1,chave2=valor2` |

No ambiente **`Testing`** (usado pelo `WebApplicationFactory` dos testes de integração) o SDK do
OpenTelemetry **não é registrado** — sem isso, a suíte tentaria exportar para um coletor
inexistente, seria mais lenta e mais ruidosa nos logs.

### Verificação local (sem ferramenta paga)

O `docker compose up` sobe também um **Jaeger all-in-one** com OTLP habilitado, já apontado pela
API (`OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4317` no `docker-compose.yml`):

```bash
docker compose up --build

# faça algumas requisições autenticadas contra a API (ex.: via Bruno/curl) e depois:
# abra a UI do Jaeger
```

- **Jaeger UI:** `http://localhost:16686` — selecione o serviço `oficina-mecanica-api`, clique em
  **Find Traces** e abra um trace: o span da requisição HTTP aparece com o(s) span(s) do banco
  (Npgsql) aninhados dentro dele, com a duração de cada um.
- Os logs (`docker compose logs api`) continuam em JSON com `trace_id`/`span_id`
  (`TraceJsonConsoleFormatter`, ADR-004) — o mesmo `trace_id` visto no log aparece na busca por
  Trace ID do Jaeger, fechando a correlação log → trace.

Em Kubernetes, `OTEL_EXPORTER_OTLP_ENDPOINT` **não está definido hoje** no
[`ConfigMap`](k8s/base/01-configmap.yaml) — o Service `otel-collector` hipotético nunca existiu no
cluster, e mantê-lo só gerava erro de exportação em loop nos logs; sem a variável, o SDK cai no
default `http://localhost:4317` e simplesmente não exporta. Quando a ferramenta de APM (New
Relic/Datadog, RFC-004) for escolhida, essa chave volta ao `ConfigMap` apontando para o destino
real (e, se precisar de chave de API, ela vai como `OTEL_EXPORTER_OTLP_HEADERS` no `Secret`
`oficina-secrets` — nunca em texto plano no ConfigMap).

---

## APIs — Documentação e Collection

| Recurso | Onde |
|---|---|
| **Documentação interativa (Scalar/OpenAPI)** | `docker compose up`: `http://localhost:8080/scalar` (e `/openapi`) — nuvem: `<url-da-api>/scalar`, onde `<url-da-api>` é descoberta com o comando em [Deploy na Nuvem (AWS)](#deploy-na-nuvem-aws) |
| **Collection completa (Bruno)** | [`docs/guias/collection_bruno.yml`](docs/guias/collection_bruno.yml) — importável no [Bruno](https://usebruno.com), ambiente `Local` pré-configurado |

A collection Bruno inclui todos os módulos e endpoints. Ela usa **duas variáveis de token** no
ambiente `Local`: `token_oficina` (herdado por todas as requisições) e `token_cliente`, que
sobrescreve o header apenas em **Aprovar Orçamento**, **Rejeitar Orçamento** e **Listar para
Acompanhamento** — as três rotas de papel `Cliente`. Preencha `token_oficina` com o campo `token`
da resposta de **Autenticacao > Login**; `token_cliente` precisa ser gerado manualmente enquanto a
Function Serverless de autenticação por CPF não existir (ver seção
[Autenticação e autorização](#autenticação-e-autorização)).

A exposição do Scalar/OpenAPI **não depende mais do ambiente** (`ASPNETCORE_ENVIRONMENT`) — é
controlada pela flag `OpenApi:Enabled` (variável `OpenApi__Enabled`), habilitada por padrão em
[`appsettings.json`](src/Bootstrap/Api/appsettings.json) e propagada explicitamente no
[`docker-compose.yml`](docker-compose.yml) e no [`ConfigMap`](k8s/base/01-configmap.yaml) do
Kubernetes. Isso permite consultar a documentação também no ambiente publicado (kind), e desligá-la
sem recompilar (`OpenApi__Enabled=false`) caso um deploy mais restritivo precise disso. Os testes
de integração desligam a flag explicitamente para manter a suíte rápida e sem rotas de
documentação registradas.

---

## Testes

### Guias E2E

- **[Cenário feliz (happy path)](docs/guias/teste-cenario-feliz.md)** — fluxo completo com
  exemplos `curl`, resultados esperados e checklist.
- **[Cenários alternativos](docs/guias/teste-cenarios-alternativos.md)** — validações de erro:
  CPF/CNPJ inválido, placa inválida, transições inválidas, estoque insuficiente, rejeição + estorno.

### Testes automatizados

```bash
# Todos (unitários + integração)
dotnet test OficinaMecanica.slnx

# Apenas unitários (sem Docker)
dotnet test OficinaMecanica.slnx --filter "Category!=Integration"

# Apenas integração (requer Docker — Testcontainers.PostgreSql)
dotnet test tests/IntegrationTests
```

| Camada | Ferramenta |
|---|---|
| Framework | xUnit |
| Mocks | Moq |
| Integração (banco real) | Testcontainers.PostgreSql |
| Cobertura | Coverlet + ReportGenerator |

**Domain.Tests** — puros, sem IO, sustentam a cobertura ≥ 80%. **IntegrationTests** — sobem a
aplicação completa com `WebApplicationFactory<Program>` e Postgres real, validando migrations,
adapters e o pipeline de eventos de ponta a ponta.

### Cobertura (meta ≥ 80%)

```bash
dotnet test OficinaMecanica.slnx --collect:"XPlat Code Coverage" --results-directory coverage-results/
reportgenerator -reports:"coverage-results/**/coverage.cobertura.xml" -targetdir:"coverage-report/" -reporttypes:Html
```

![Coverage Summary](docs/images/coverage-summary.png)
![Coverage Detail](docs/images/coverage-detail.png)

---

## Vídeo Demonstrativo

> 🎥 **[Assista à demonstração no YouTube](https://youtu.be/ZnaUtgUUb5I)** _(https://youtu.be/ZnaUtgUUb5I)_

O vídeo (≤ 15 min, público ou não listado) demonstra:

- Deploy da aplicação (Terraform provisiona o cluster e sobe a stack)
- Execução do CI/CD (pipeline no GitHub Actions)
- Consumo das APIs (via Scalar / Bruno)
- **Escalabilidade automática** — carga na API dispara o HPA e escala as réplicas

---

## Documentação Completa

Índice navegável de toda a documentação: **[`docs/README.md`](docs/README.md)**.

| Tema | Pasta |
|---|---|
| Arquitetura, diagramas e decisões | [`docs/arquitetura/`](docs/arquitetura/) |
| Planos de implementação e infra | [`docs/planos/`](docs/planos/) |
| Guias de teste e collection | [`docs/guias/`](docs/guias/) |
| Terraform do cluster/rede/ECR (passo a passo) | [`oficina-mecanica-infra-k8s`](https://github.com/gabrielMauad/oficina-mecanica-infra-k8s) |
| Terraform do banco gerenciado (passo a passo) | [`oficina-mecanica-infra-db`](https://github.com/gabrielMauad/oficina-mecanica-infra-db) |
| Terraform e pipeline da Function Serverless | [`oficina-mecanica-lambda-auth`](https://github.com/gabrielMauad/oficina-mecanica-lambda-auth) |
| Enunciados oficiais (FIAP) | [`docs/spec/`](docs/spec/) |

---

## Licença

Ver [`LICENSE`](LICENSE).
