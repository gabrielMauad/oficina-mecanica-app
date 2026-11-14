# Desenho — Infraestrutura

> Existem **dois ambientes**: o **local de desenvolvimento** (`docker compose`, sem Kubernetes) e o
> **de produção, na nuvem** — AWS Academy Learner Lab. A infraestrutura de nuvem é persistente e
> gerenciada por repositórios Terraform próprios, não recriada a cada execução do CI. Na Fase 4 ela
> sustenta **cinco serviços** (um Deployment, um ECR e um listener da NLB cada), três bancos
> PostgreSQL, uma tabela DynamoDB e a mensageria SNS/SQS.
>
> Diagramas em [Mermaid](https://mermaid.js.org/) — renderizam nativamente no GitHub.

---

## Ambiente local de desenvolvimento

Cada repositório de serviço tem o **próprio `docker-compose.yml`**, com o serviço e as dependências
exclusivas dele (o PostgreSQL próprio). As dependências **compartilhadas** ficam em
[`docker-compose.infra.yml`](../../../docker-compose.infra.yml), neste repositório: ele cria a rede
Docker `oficina-local` e sobe o **LocalStack** (SNS, SQS e DynamoDB) e o **Jaeger** (coletor OTLP +
UI). Ordem: `docker compose -f docker-compose.infra.yml up -d`, depois o `docker compose up` de cada
serviço desejado. Os serviços se chamam por nome de contêiner na rede `oficina-local`.

```mermaid
flowchart TB
    dev["Desenvolvedor"]

    subgraph infra["docker-compose.infra.yml (neste repositório) — rede oficina-local"]
        direction LR
        ls["oficina-localstack<br/>SNS, SQS, DynamoDB — :4566"]
        jg["oficina-jaeger<br/>OTLP :4317/:4318, UI :16686"]
    end

    subgraph os["docker-compose.yml — ordens-servico"]
        osC["oficina-ordens-servico :8080"]
        osDb[("Postgres :5432")]
    end
    subgraph cad["docker-compose.yml — cadastro"]
        cadC["oficina-cadastro :8082"]
        cadDb[("Postgres :5433")]
    end
    subgraph pec["docker-compose.yml — pecas-insumos"]
        pecC["oficina-pecas-insumos :8083"]
        pecDb[("Postgres :5434")]
    end
    pagC["oficina-pagamento :8084<br/>(docker-compose.yml do repositório)"]
    authC["oficina-autenticacao :8081<br/>(docker-compose.yml do repositório)"]

    dev -->|"docker compose up"| infra
    dev -->|"docker compose up"| os
    dev -->|"docker compose up"| cad
    dev -->|"docker compose up"| pec
    dev -->|"docker compose up"| pagC
    dev -->|"docker compose up"| authC

    osC --- osDb
    cadC --- cadDb
    pecC --- pecDb
    osC -->|"HTTP"| cadC
    osC -->|"HTTP"| pecC
    osC -.->|"eventos"| ls
    pecC -.->|"eventos"| ls
    pagC -.->|"eventos + tabela DynamoDB"| ls
    osC -.->|"OTLP"| jg
    cadC -.->|"OTLP"| jg
    pecC -.->|"OTLP"| jg
    pagC -.->|"OTLP"| jg
    authC -.->|"OTLP"| jg
```

Sem Kubernetes localmente — a validação dos manifestos acontece contra o cluster de nuvem. Valores de
desenvolvimento (banco `oficina` / `oficina-dev-pass`, segredo JWT de desenvolvimento, credenciais
do mock do Mercado Pago) estão nos `docker-compose.yml`.

---

## Ambiente de produção: nuvem (AWS Academy Learner Lab)

Provedor decidido no [RFC-002](../rfcs/002-escolha-do-provedor-de-nuvem.md) (**AWS**, conta AWS
Academy Learner Lab); tudo provisionado por Terraform.

```mermaid
flowchart TB
    ator["Atendente / Cliente"]
    equipe["Equipe do projeto<br/>(e-mail)"]

    subgraph aws["AWS Academy Learner Lab — us-east-1"]
        direction TB
        gw["API Gateway (HTTP API) oficina-mecanica-api<br/>rotas de negócio por prefixo + rota padrão + rotas técnicas por serviço"]

        subgraph vpc["VPC default da conta (subnets públicas, sem NAT Gateway)"]
            direction TB
            authFn["Lambda de autenticação por CPF<br/>oficina-mecanica-lambda-auth<br/>(não instrumentada, sem NAT/internet)"]
            nlb["NLB interna — um listener TCP por serviço<br/>30080 ordens-servico · 30081 autenticacao<br/>30082 cadastro · 30083 pecas-insumos · 30084 pagamento"]

            subgraph eks["Cluster EKS (oficina-mecanica) — namespace oficina-mecanica<br/>node group t3.medium, 2–4 nós (desejado: 3)"]
                direction LR
                os["ordens-servico<br/>HPA 1–3"]
                auth["autenticacao<br/>HPA 1–3"]
                cad["cadastro<br/>HPA 1–3"]
                pec["pecas-insumos<br/>HPA 1–3"]
                pag["pagamento<br/>HPA 1–3"]
                nri["nri-bundle (Helm)<br/>namespace newrelic"]
            end

            rdsOs[("RDS ordens-servico<br/>db ordens_servico")]
            rdsCad[("RDS cadastro<br/>db cadastro")]
            rdsPec[("RDS pecas-insumos<br/>db pecas_insumos")]
        end

        dyn[("DynamoDB<br/>oficina-mecanica-dev-pagamentos")]
        broker{{"SNS + SQS<br/>tópicos, filas e assinaturas criados pelo MassTransit"}}
        secrets["Secrets Manager<br/>oficina-mecanica/dev/app<br/>oficina-mecanica/dev/rds/&lt;serviço&gt; (×3)"]
        ecr["Amazon ECR<br/>oficina-mecanica-&lt;serviço&gt; (×5)"]
        apm["New Relic<br/>APM, dashboards e alertas (RFC-004)"]
    end

    ator -->|"HTTPS/JSON"| gw
    gw -->|"POST /auth/cpf"| authFn
    gw -->|"demais rotas, via VPC Link"| nlb
    nlb -->|"NodePort do serviço"| os & auth & cad & pec & pag
    authFn -->|"GET /clientes/documento/{cpf}<br/>NLB :30082, JWT de serviço"| nlb
    os -->|"HTTP (DNS interno do cluster)"| cad
    os -->|"HTTP (DNS interno do cluster)"| pec
    os -->|"Npgsql, SSL"| rdsOs
    cad -->|"Npgsql, SSL"| rdsCad
    pec -->|"Npgsql, SSL"| rdsPec
    pag -->|"AWSSDK"| dyn
    os -.->|"eventos"| broker
    pec -.->|"eventos"| broker
    pag -.->|"eventos"| broker
    authFn -.->|"lê segredos no apply (Terraform)"| secrets
    eks -.->|"lê segredos no deploy (pipeline)"| secrets
    ecr -.->|"imagens publicadas"| eks
    eks -.->|"OTLP http/protobuf :4318: traces + métricas + logs"| apm
    nri -.->|"CPU/memória de nós e pods"| apm
    apm -.->|"Synthetic (Ping): GET /healthz/ready"| gw
    apm -.->|"alertas (workflow → e-mail)"| equipe

    classDef existente fill:#1168bd,stroke:#0b4884,color:#fff
    classDef pessoa fill:#08427b,stroke:#052e56,color:#fff
    class ator,equipe pessoa
    class gw,vpc,eks,os,auth,cad,pec,pag,rdsOs,rdsCad,rdsPec,dyn,broker,secrets,ecr,authFn,nlb,aws,apm,nri existente
```

### Roteamento do API Gateway

| Serviço | Rotas de negócio (sem reescrita de caminho) |
|---|---|
| autenticacao | `ANY /api/v1/auth/{proxy+}` |
| cadastro | `ANY /api/v1/clientes[/{proxy+}]`, `ANY /api/v1/veiculos[/{proxy+}]`, `ANY /api/v1/servicos[/{proxy+}]` |
| pecas-insumos | `ANY /api/v1/pecas-insumos[/{proxy+}]` |
| ordens-servico | `ANY /api/v1/ordens-servico[/{proxy+}]` e a rota **padrão** `ANY /{proxy+}` |
| pagamento | `ANY /api/v1/pagamentos/{proxy+}` |
| Lambda | `POST /auth/cpf` |

Cada serviço tem ainda uma **rota técnica com reescrita de caminho**, `ANY /<serviço>/{proxy+}`
(ex.: `GET /cadastro/healthz/ready` chega ao pod como `GET /healthz/ready`), usada pelos smoke tests e
para abrir o `openapi/v1.json` de cada serviço. Não há rota própria para `/mock/mercadopago/*`; porém
a rota técnica do Pagamento também o alcança — o mock exige as credenciais do cliente e um Bearer
(limitação registrada no README do serviço).

### Segredos, imagens e Kubernetes

- **Secrets Manager:** `oficina-mecanica/dev/app` (segredo JWT, senha do admin, credenciais do mock do
  Mercado Pago — criado por `infra-k8s`) e `oficina-mecanica/dev/rds/<serviço>` (um por banco —
  criados por `infra-db`). A pipeline de cada serviço monta o `Secret` `<serviço>-secrets` no deploy;
  nenhum segredo é commitado.
- **ECR:** um repositório por serviço (`oficina-mecanica-<serviço>`); a tag da imagem é o SHA do commit.
- **Kubernetes:** namespace `oficina-mecanica` compartilhado; por serviço, ConfigMap, Deployment,
  Service **NodePort** (80 → 8080), HPA de 1 a 3 réplicas a 50% de CPU, probes `/healthz/live` e
  `/healthz/ready`, requests 100m/256Mi e limits 500m/512Mi. O `nri-bundle` é instalado apenas pela
  pipeline do `ordens-servico`.
- **Mensageria:** SNS/SQS são criados pelo próprio MassTransit na subida de cada serviço; os pods usam
  a cadeia padrão de credenciais do SDK (role do nó do EKS), sem endpoint customizado.
- **Dimensionamento:** `t3.medium`; o fator limitante é o limite de pods por nó e a memória. Três nós
  acomodam os cinco serviços com até três réplicas cada, os pods de sistema e o `nri-bundle`.

### Topologia de repositórios

O Terraform se divide em três repositórios — `oficina-mecanica-infra-k8s` (cluster, rede, NLB, API
Gateway, ECR por serviço, segredo da aplicação), `oficina-mecanica-infra-db` (três RDS PostgreSQL, a
tabela DynamoDB e os segredos dos bancos) e `oficina-mecanica-lambda-auth` (a Function, seu security
group e sua rota no API Gateway) — que publicam outputs consumidos entre si via
`terraform_remote_state`. Nenhum cria VPC própria: todos usam a VPC default da conta via `data`
sources ([RFC-002](../rfcs/002-escolha-do-provedor-de-nuvem.md), "Atualização — VPC default"). O
código, a imagem e os manifestos de cada serviço ficam no repositório do próprio serviço
([ADR-005](../adrs/005-quatro-repositorios-e-estrategia-de-branches.md),
[ADR-009](../adrs/009-decomposicao-em-microsservicos.md)).

**Diferenças em relação ao ambiente local:**

| Peça | Local (`docker compose`) | Nuvem (produção) |
|---|---|---|
| Orquestração | Nenhuma (contêineres soltos) | **EKS**, com HPA por serviço |
| Bancos PostgreSQL | um contêiner por serviço, volume Docker | **uma instância RDS por serviço**, gerenciada, fora do cluster |
| NoSQL (Pagamento) | DynamoDB no **LocalStack** | **DynamoDB** (tabela criada pelo Terraform) |
| Mensageria | SNS/SQS no **LocalStack** (`Mensageria__ServiceUrl`) | **SNS/SQS** reais, credenciais do nó |
| Entrada de tráfego | portas expostas no host (8080–8084) | **API Gateway** → VPC Link → NLB interna |
| Autenticação por CPF | JWT assinado manualmente (sem a Function) | **Lambda** real, atrás do API Gateway |
| Segredos | variáveis de ambiente do `docker-compose.yml` | **Secrets Manager**, resolvidos no apply/deploy |
| Telemetria (destino) | Jaeger local | **New Relic** — OTLP + `nri-bundle` (RFC-004) |
| Terraform | não se aplica | state remoto (S3), um repositório por peça de infraestrutura |

A aplicação exporta OTLP e expõe health checks igualmente nos dois ambientes — só mudam o destino do
OTLP e o que está por trás do Kubernetes. Ver
[Observabilidade no README](../../../README.md#observabilidade-opentelemetry--new-relic) e
[`metricas.md`](../metricas.md).
