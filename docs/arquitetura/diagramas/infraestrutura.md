# Desenho — Infraestrutura

> Existem **dois ambientes** neste projeto: o **local de desenvolvimento** (`docker compose`, sem
> Kubernetes) e o **de produção, na nuvem** — AWS Academy Learner Lab, exigido pela Fase 3.
> O cluster **kind** local usado na Fase 2 (e na pipeline de CI/CD daquela fase) foi removido: a
> infraestrutura de nuvem é persistente e gerenciada pelos repositórios de infra, não recriada a
> cada execução do CI.
>
> Diagramas em [Mermaid](https://mermaid.js.org/) — renderizam nativamente no GitHub.

---

## Ambiente local de desenvolvimento

Uma única forma de rodar o projeto localmente: **`docker compose up`**, para desenvolvimento e
testes manuais do dia a dia. Sobe `postgres`, `api` e `jaeger` (coletor OTLP + UI, ver
[Observabilidade no README](../../../README.md#observabilidade-opentelemetry)). Sem Kubernetes —
a validação de manifestos Kubernetes acontece hoje contra o cluster de nuvem, não localmente.

```mermaid
flowchart TB
    dev["Desenvolvedor"]

    subgraph compose["docker compose (dia a dia)"]
        direction LR
        apiC["api<br/>.NET 10, porta 8080"]
        pgC[("postgres<br/>PostgreSQL 16, porta 5432")]
        jaeger["jaeger<br/>coletor OTLP + UI, porta 16686"]
        apiC -->|"EF Core / Npgsql"| pgC
        apiC -.->|"OTLP gRPC :4317"| jaeger
    end

    dev -->|"docker compose up"| compose

    classDef existente fill:#1168bd,stroke:#0b4884,color:#fff
    class apiC,pgC,jaeger existente
```

> Detalhes de decisão do cluster kind removido (Fase 2) em
> [`../../planos/infra-fase-2/00-visao-geral.md`](../../planos/infra-fase-2/00-visao-geral.md) —
> documento histórico, mantido como registro do que existiu, não como estado atual.

---

## Ambiente de produção: nuvem (AWS Academy Learner Lab)

A Fase 3 exige infraestrutura de nuvem de verdade: API Gateway, Function Serverless, banco de
dados gerenciado, cluster Kubernetes gerenciado com escalabilidade e tudo provisionado por
Terraform — decisão de provedor em [RFC-002](../rfcs/002-escolha-do-provedor-de-nuvem.md)
(**AWS**, conta AWS Academy Learner Lab).

```mermaid
flowchart TB
    ator["Atendente / Cliente"]
    equipe["Equipe do projeto<br/>(e-mail)"]

    subgraph aws["AWS Academy Learner Lab — us-east-1"]
        direction TB
        gw["API Gateway (HTTP API)<br/>roteamento e controle de acesso"]

        subgraph vpc["VPC default da conta (subnets publicas, sem NAT Gateway)"]
            direction TB
            authFn["Function Serverless de autenticacao<br/>oficina-mecanica-lambda-auth<br/>(nao instrumentada, sem NAT/internet)"]

            subgraph eks["Cluster EKS (oficina-mecanica)<br/>node group t3.small, 2-3 nos"]
                direction TB
                api["Aplicacao .NET 10<br/>oficina-mecanica-app<br/>HPA 1-5 @ 50% CPU"]
                nri["nri-bundle (Helm)<br/>namespace newrelic<br/>DaemonSet + kube-state-metrics"]
            end

            nlb["NLB interna<br/>target group: NodePort 30080"]
            rds[("RDS PostgreSQL 16<br/>oficina-mecanica-infra-db<br/>db.t3.micro, single-AZ")]
        end

        secrets["Secrets Manager<br/>segredo do RDS + segredo da aplicacao (JWT/admin)"]
        ecr["Amazon ECR<br/>oficina-mecanica-api"]
        apm["New Relic<br/>APM, dashboards e alertas (RFC-004)"]
    end

    ator -->|"HTTPS/JSON"| gw
    gw -->|"POST /auth/cpf"| authFn
    gw -->|"demais rotas, via VPC Link"| nlb
    nlb -->|"NodePort 30080"| api
    authFn -->|"consulta cadastro.cliente (somente leitura)"| rds
    api -->|"EF Core / Npgsql, SSL"| rds
    authFn -.->|"le segredos no apply (Terraform)"| secrets
    api -.->|"le segredos no deploy (pipeline)"| secrets
    ecr -.->|"imagem publicada"| api
    api -.->|"OTLP http/protobuf :4318: traces + metricas + logs"| apm
    nri -.->|"metricas de CPU/memoria de nos e pods"| apm
    apm -.->|"Synthetic (Ping): GET /healthz/ready"| gw
    apm -.->|"alerta de dashboard/monitor"| equipe

    classDef existente fill:#1168bd,stroke:#0b4884,color:#fff
    class ator fill:#08427b,stroke:#052e56,color:#fff
    class equipe fill:#08427b,stroke:#052e56,color:#fff
    class gw,vpc,eks,api,rds,secrets,ecr,authFn,nlb,aws,apm,nri existente
```

**Legenda:** todas as caixas deste diagrama estão **provisionadas** — nenhuma está mais pendente.
Cluster EKS, NLB, API Gateway, ECR e o segredo da aplicação nascem em
`oficina-mecanica-infra-k8s`; o RDS e seu segredo em `oficina-mecanica-infra-db`; a Function e sua
rota no API Gateway em `oficina-mecanica-lambda-auth`; a imagem, os manifestos da API e os
`values` do `nri-bundle` (`k8s/observabilidade/newrelic-values.yaml`) neste repositório
(`oficina-mecanica-app`). A ferramenta de observabilidade (`apm`) é o **New Relic**, decidido e
implementado no [RFC-004](../rfcs/004-ferramenta-de-observabilidade.md): a aplicação exporta
traces, métricas e logs por OTLP (`http/protobuf`, `otlp.nr-data.net:4318`); o `nri-bundle`
(Helm, namespace `newrelic`) publica CPU/memória de nós e pods; um monitor sintético (Ping,
`oficina-mecanica-healthz`) verifica `GET /healthz/ready` através do próprio API Gateway; e
dashboards/alertas notificam a equipe por e-mail (ver `docs/observabilidade/`). A **Function
Serverless não é instrumentada**: roda em subnet sem NAT Gateway, sem alcance à internet para
exportar OTLP (ADR-004, RFC-004).

**Topologia de repositórios** (ver
[ADR-005](../adrs/005-quatro-repositorios-e-estrategia-de-branches.md)): o Terraform deste
ambiente se divide em três repositórios — `oficina-mecanica-infra-k8s` (cluster, rede, NLB, API
Gateway, ECR e segredo da aplicação), `oficina-mecanica-infra-db` (banco gerenciado e seu segredo)
e `oficina-mecanica-lambda-auth` (a Function, seu security group e sua rota no API Gateway) — que
publicam outputs (endpoint do banco, nome do cluster, id da API, security groups) consumidos entre
si e por `oficina-mecanica-app` via estado remoto (`terraform_remote_state`). Nenhum deles cria
VPC própria: todos usam a VPC default da conta AWS Academy Learner Lab via `data` sources — ver
[RFC-002](../rfcs/002-escolha-do-provedor-de-nuvem.md) ("Atualização — VPC default").

**Diferenças em relação ao ambiente local:**

| Peça | Local (`docker compose`) | Nuvem (produção) |
|---|---|---|
| Cluster Kubernetes | Nenhum (a API roda direto em contêiner) | **EKS**, gerenciado, com node group escalável |
| Banco de dados | PostgreSQL em contêiner, volume Docker | **RDS PostgreSQL 16**, gerenciado, fora do cluster |
| Entrada de tráfego | Porta exposta direto no host (`8080`) | **API Gateway** com roteamento e controle de acesso, via NLB interna |
| Autenticação por CPF | Assinatura manual de JWT (sem Function rodando) | **Function Serverless** real, atrás do API Gateway |
| Segredos | Variáveis de ambiente do `docker-compose.yml` | **Secrets Manager**, resolvidos no apply/deploy — nunca commitados |
| Telemetria (destino) | Jaeger local (`docker compose`) | **New Relic** — OTLP (traces, métricas e logs) + `nri-bundle` para CPU/memória do cluster (RFC-004) |
| Terraform | Não se aplica | State remoto (S3), um repositório por peça de infraestrutura |

A aplicação já exporta OTLP e já expõe health checks — isso **não muda** entre os dois ambientes,
só o destino do OTLP e o que está por trás do Kubernetes muda. Ver
[Observabilidade no README](../../../README.md#observabilidade-opentelemetry) e
[`metricas.md`](../metricas.md) para o que já é emitido pela aplicação hoje, independente de onde
ela roda.
