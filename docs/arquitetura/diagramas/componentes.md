# Desenho — Componentes da Aplicação

> Modelo C4 (níveis 1 → 4) do **Sistema de Oficina Mecânica**, um _Modular Monolith_ em
> .NET 10 com quatro Bounded Contexts e aderência à Clean Architecture. O nível 4
> (implantação em nuvem) mistura o que já está implementado com o que é alvo da Fase 3 —
> ver a legenda na respectiva seção.
> Diagramas em [Mermaid](https://mermaid.js.org/) — renderizam nativamente no GitHub.
>
> Documentação de apoio: [`estrutura-do-projeto.md`](../estrutura-do-projeto.md),
> [`clean-architecture.md`](../clean-architecture.md).

---

## Nível 1 — Contexto

Quem usa o sistema e com o que ele fala. O sistema é um back-end único (não há integrações
externas reais: e-mail é simulado e o barramento de eventos é in-process).

```mermaid
flowchart TB
    atendente["Atendente / Mecânico"]
    cliente["Cliente"]
    sistema["Sistema de Oficina Mecânica<br/>Back-end REST .NET 10"]
    db[("PostgreSQL 16<br/>1 schema por módulo")]

    authFn["Function Serverless de autenticação<br/>emite token por CPF (repositório à parte)"]

    atendente -->|"HTTPS/JSON, token papel Oficina<br/>(login por email + senha na própria API)"| sistema
    cliente -->|"HTTPS/JSON, token papel Cliente<br/>(acompanhamento e decisão do orçamento)"| sistema
    cliente -->|"autentica por CPF"| authFn
    authFn -.->|"token assinado com o mesmo segredo (HS256)"| cliente
    sistema -->|"EF Core / Npgsql"| db
    authFn -->|"consulta cadastro.cliente (somente leitura)"| db

    classDef person fill:#08427b,stroke:#052e56,color:#fff
    classDef system fill:#1168bd,stroke:#0b4884,color:#fff
    classDef store fill:#438dd5,stroke:#2e6295,color:#fff
    class atendente,cliente person
    class sistema,authFn system
    class db store
```

**Dois emissores de token, autorização por papel.** A aplicação emite o token do atendente
(`POST /api/v1/auth/login`, papel `Oficina`); a Function Serverless emite o token do cliente a
partir do CPF (papel `Cliente`). Ambos são HS256 com o mesmo segredo e distinguidos por `iss` e
pela claim `role` — ver [RFC-001](../rfcs/001-estrategia-de-autenticacao.md),
[ADR-001](../adrs/001-jwt-hs256-segredo-compartilhado.md) e
[ADR-003](../adrs/003-dois-emissores-e-autorizacao-por-papel.md). A consulta da OS pelo cliente
**deixou de ser pública**: hoje é autenticada e restrita ao dono da OS.

---

## Nível 2 — Containers (módulos)

O monólito roda em **um único container** (`Bootstrap/Api`), mas internamente é dividido em
quatro Bounded Contexts com fronteiras **físicas** (assembly próprio por camada por módulo) —
não em camadas horizontais. O `SharedKernel` fornece os tipos-base e o pipeline transversal.

![Diagrama C4 de Containers](../../images/C4%20-%20Containers.png)

**Comunicação entre módulos** (nenhum módulo referencia Domain/Application de outro):

| Tipo | Quando | Mecanismo |
|---|---|---|
| **Síncrona (ACL)** | precisa de resposta imediata (ex.: cliente existe ao abrir OS) | _port_ no Domain do consumidor + _adapter_ na Infrastructure consumindo os `Contracts` do produtor |
| **Assíncrona (Integration Events)** | fato consumado que outro BC reage (ex.: orçamento gerado → baixa estoque) | evento em `<Modulo>.Contracts` publicado no `IIntegrationEventBus` após o commit |

---

## Nível 3 — Componentes de um módulo (Clean Architecture)

Recorte do módulo **OrdemServico** (o mais completo). Cada anel da Clean Architecture é um
projeto físico; a Regra de Dependência é forçada em compile-time — as setas apontam sempre
para dentro.

![Diagrama C4 de Componentes](../../images/C4%20-%20Componentes.png)

**Persistência sem acoplar o Domain (DTOs de persistência):** o EF Core mapeia **Records**
(`OrdemServicoRecord`, etc., em `Adapters/DataSources/Records`), **nunca o agregado de Domain**.
O `DbContext`, as `Configurations` e o `Repository` da Infrastructure só conhecem Records; o
`Mapper` (`Adapters/DataSources/Mappers`) converte Record ↔ Domain dentro do Gateway. Assim o
Domain não tem nenhuma referência a EF/Npgsql. _(refatoração da Fase 2 — ver
[`../../planos/refatoracao-clean-architecture/07-plano-dtos-persistencia.md`](../../planos/refatoracao-clean-architecture/07-plano-dtos-persistencia.md).)_

**Por que Contracts é um pacote à parte (fora dos 4 anéis):** `OrdemServico.Contracts` é a
**interface pública publicada do módulo** — queries síncronas, DTOs e integration events que
**outros** Bounded Contexts consomem. Não é Domain (não tem entidades), nem Application (não tem
use cases), nem Adapters/Infrastructure. Depende **apenas de `SharedKernel.Domain`** e por isso é
um projeto separado, referenciado tanto por este módulo (a Application publica os integration
events; a Infrastructure implementa as queries) quanto pelos módulos consumidores. É o
equivalente à "linguagem publicada" do BC.

**Regra de Dependência** — referências permitidas por camada:

| Camada | Depende de |
|---|---|
| **Domain** | `SharedKernel.Domain` apenas |
| **Application** | Domain, SharedKernel.*, Contracts (próprios e de outros módulos) |
| **Adapters** | Application, Domain, Contracts, MediatR — **sem ASP.NET/EF** |
| **Contracts** | `SharedKernel.Domain` apenas |
| **Infrastructure** | Application, Domain, Contracts, Adapters, EF/Npgsql |
| **Web** | Adapters, SharedKernel.* |

> Detalhamento em [`estrutura-do-projeto.md`](../estrutura-do-projeto.md) e
> [`clean-architecture.md`](../clean-architecture.md).

---

## Nível 4 — Implantação em Nuvem (alvo da Fase 3)

A Fase 3 exige que este diagrama de componentes ganhe uma **visão de nuvem**: API Gateway, banco
de dados, Kubernetes, Function Serverless e monitoramento. O diagrama abaixo é uma visão de
**implantação** (deployment), não mais de código — mostra onde cada componente roda e como eles se
falam quando o ambiente de nuvem existir.

```mermaid
flowchart TB
    atendente["Atendente / Mecânico"]
    cliente["Cliente"]

    subgraph nuvem["Nuvem — provedor a definir<br/>(AWS é hipótese de trabalho; decisão formal no RFC-002, pendente)"]
        direction TB
        gateway["API Gateway<br/>roteamento e controle de acesso"]
        authFn["Function Serverless de autenticação<br/>valida CPF, consulta cadastro.cliente, emite JWT<br/>repositório próprio: oficina-mecanica-lambda-auth<br/>(não instrumentada, sem NAT/internet)"]
        subgraph k8s["Cluster Kubernetes gerenciado"]
            direction TB
            app["Aplicação .NET 10<br/>monólito modular, 4 Bounded Contexts"]
            nri["nri-bundle (Helm)<br/>namespace newrelic"]
        end
        db[("Banco de dados gerenciado<br/>PostgreSQL, 1 schema por módulo")]
        apm["New Relic<br/>APM, dashboards e alertas (RFC-004)"]
        equipe["Equipe do projeto<br/>(e-mail)"]
    end

    atendente -->|"HTTPS/JSON, token papel Oficina"| gateway
    cliente -->|"HTTPS/JSON, token papel Cliente"| gateway
    cliente -->|"autentica por CPF"| gateway
    gateway -->|"rota pública /auth"| authFn
    gateway -->|"rotas protegidas, valida JWT"| app
    authFn -.->|"token assinado (HS256)"| cliente
    authFn -->|"consulta cadastro.cliente (somente leitura)"| db
    app -->|"EF Core / Npgsql"| db
    app -.->|"OTLP http/protobuf: traces + métricas + logs"| apm
    nri -.->|"métricas de CPU/memória de nós e pods"| apm
    apm -.->|"Synthetic (Ping): GET /healthz/ready"| gateway
    apm -.->|"alerta de dashboard/monitor"| equipe

    classDef person fill:#08427b,stroke:#052e56,color:#fff
    classDef existente fill:#1168bd,stroke:#0b4884,color:#fff
    classDef alvo fill:#ffffff,stroke:#999999,color:#555555,stroke-dasharray: 5 5

    class atendente,cliente person
    class app,authFn,apm,nri,equipe existente
    class gateway,db,k8s,nuvem alvo
```

**Legenda:** caixa azul sólida = **implementado hoje**; caixa branca de borda tracejada = **alvo
da Fase 3, ainda não provisionado**.

**O que já existe:** a **aplicação** (`app`) — o mesmo monólito modular dos níveis 1 a 3 — e a
**Function Serverless de autenticação** (`authFn`) têm código e testes prontos em seus
repositórios (ver [ADR-005](../adrs/005-quatro-repositorios-e-estrategia-de-branches.md)). A
aplicação já emite traces, métricas e logs via OpenTelemetry/OTLP e já expõe health checks — esse
tráfego já vai para o **New Relic** (`apm`) em produção, decisão e implementação do
[RFC-004](../rfcs/004-ferramenta-de-observabilidade.md); ver
[`infraestrutura.md`](infraestrutura.md) para o desenho completo desse fluxo. A **Function
Serverless não é instrumentada**: roda em subnet sem NAT Gateway, sem alcance à internet para
exportar OTLP (ADR-004, RFC-004).

**O que é alvo, ainda não provisionado:** **API Gateway**, **cluster Kubernetes gerenciado**
(hoje é kind local) e **banco de dados gerenciado** (hoje é PostgreSQL em pod). Nenhum desses três
itens tem Terraform de nuvem escrito ainda — ver
[`docs/planos/fase-3/00-analise-da-spec.md`](../../planos/fase-3/00-analise-da-spec.md), seção
3.1, para o inventário completo de recursos e o que falta.

**Sobre o provedor:** o diagrama usa vocabulário genérico (API Gateway, banco gerenciado, cluster
Kubernetes gerenciado) porque a escolha de nuvem **ainda não é uma decisão formal** — o RFC-002
está pendente. AWS aparece nos planos como hipótese de trabalho (API Gateway, EKS, RDS), mas este
diagrama não deve ser lido como confirmação de que a nuvem já foi escolhida.
