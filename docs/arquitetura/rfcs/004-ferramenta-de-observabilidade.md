# RFC-004 — Ferramenta de observabilidade

- **Status:** Aprovado
- **Data:** 2026-09-10
- **Decisões relacionadas:** [ADR-004 — Correlação via traceId W3C](../adrs/004-correlacao-via-traceid-w3c.md)
- **Relacionado:** [RFC-002 — Escolha do provedor de nuvem](002-escolha-do-provedor-de-nuvem.md)

---

## 1. Contexto

A Fase 3 exige monitoramento de **latência das APIs**, **consumo de CPU/memória do Kubernetes**,
**healthchecks e disponibilidade**, **alertas para falhas no processamento de ordens de serviço**,
**logs estruturados correlacionados** e **traces em execução** — tudo demonstrável em vídeo, com
dashboards de **volume diário de OS**, **tempo médio por etapa** (diagnóstico, execução,
finalização) e **erros de integração**. O enunciado deixa a escolha da ferramenta livre entre
**Datadog** ou **New Relic**, ou qualquer outra que atenda aos requisitos.

Esta decisão foi **deliberadamente adiada** até agora — ver a seção "Questões em aberto" do
RFC-002. Duas peças já foram construídas sem esperar por ela:

- **[ADR-004](../adrs/004-correlacao-via-traceid-w3c.md)** decidiu a correlação (trace id do W3C,
  amostragem 100%, instrumentação ponta a ponta) independentemente de qual APM vai consumir esses
  dados.
- **[`ObservabilityExtensions.cs`](../../../src/Bootstrap/Api/Extensions/ObservabilityExtensions.cs)**
  já instrumenta a aplicação com o SDK do OpenTelemetry (traces + métricas) e exporta **apenas por
  OTLP**, lendo destino e credenciais das variáveis de ambiente padrão do próprio OpenTelemetry
  (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS`). Não há SDK, agente ou linha de
  código específica de New Relic, Datadog ou qualquer outro fornecedor. O comentário no topo do
  arquivo é explícito sobre isso: "a escolha do APM continue em aberto".
- As **métricas de negócio** (`docs/arquitetura/metricas.md`) — volume de OS, duração por etapa,
  falhas de integração — já existem como instrumentos nativos do .NET
  (`System.Diagnostics.Metrics`), registrados nos mesmos `Meter`s que o OpenTelemetry coleta via
  `AddMeter(ApplicationMeterNames)` em `ObservabilityExtensions.cs`. Elas não sabem, e não
  precisam saber, qual ferramenta vai lê-las.

Ou seja: **o adiamento não deixou a aplicação sem observabilidade** — deixou a aplicação pronta
para qualquer backend que fale OTLP, o que é o argumento central deste RFC (seção 3). O que falta
decidir é só o destino: quem recebe esse OTLP, com que custo, e com que esforço de configuração de
dashboards e alertas.

## 2. Requisitos que guiam a escolha

| Requisito | Fonte |
|---|---|
| Latência de API, CPU/memória do cluster, healthcheck/disponibilidade | Spec, item 1.4 |
| Alertas para falha no processamento de OS | Spec, item 1.4 |
| Logs estruturados correlacionados, traces em execução | Spec, item 1.4; ADR-004 |
| Dashboard: volume diário de OS, tempo médio por etapa, erros de integração | Spec, item 1.4; `metricas.md` |
| Suporte nativo a OTLP (nenhum código/agente proprietário na aplicação) | Consequência de ADR-004 e `ObservabilityExtensions.cs` |
| Plano gratuito **permanente**, não trial | Ver seção 4 — decisivo para este projeto |
| Baixo esforço de instalação no cluster e de montagem dos dashboards | Prazo de entrega da fase |

O critério do plano gratuito merece destaque: o ambiente de nuvem deste projeto é efêmero por
desenho (RFC-002, seção 6.3 — provisionar, gravar o vídeo, destruir), mas a **conta na ferramenta
de observabilidade não é**. Ela precisa continuar acessível entre a gravação do vídeo e a correção
do trabalho, período que não é totalmente controlado pelo grupo. Um trial de 14 dias que expira
nesse intervalo é um risco real de a correção não conseguir ver os dashboards prometidos — não um
detalhe de rodapé.

## 3. O argumento central: a decisão não bloqueia nem é bloqueada pelo código

Toda a instrumentação de traces e métricas do lado da aplicação passa por
`AddOtlpExporter()` (duas vezes: uma em `WithTracing`, uma em `WithMetrics`), sem nenhum branch de
código por fornecedor. Trocar de ferramenta de observabilidade é trocar duas variáveis de ambiente:

```
OTEL_EXPORTER_OTLP_ENDPOINT=<endpoint do coletor ou do backend escolhido>
OTEL_EXPORTER_OTLP_HEADERS=<credencial daquele backend, se exigida>
```

Isso já é usado localmente: o `docker-compose.yml` aponta `OTEL_EXPORTER_OTLP_ENDPOINT` para o
**Jaeger** subido no compose (README, seção Observabilidade), e a troca para um backend de
produção não exige recompilar nem tocar em `ObservabilityExtensions.cs` — só mudar o valor da
variável no `ConfigMap`/`Secret` do Kubernetes. O mesmo vale para os logs: `TraceJsonConsoleFormatter`
grava `trace_id`/`span_id` em JSON na saída padrão do container, formato que qualquer coletor de
logs (CloudWatch, um forwarder de agente, ou o `stdout` lido por um DaemonSet) processa sem
adaptação.

Essa é a razão pela qual a decisão pôde ser adiada sem custo: nenhum dos backends avaliados abaixo
exige revisitar código já escrito. A escolha entre eles se resume a operação (como o
agente/coletor entra no cluster, seção 6), custo (seção 4) e conveniência de dashboards (seção 5) —
nunca a instrumentação.

## 4. Alternativas — custo e permanência do plano gratuito

Pesquisado em 2026-09-10; páginas de preço mudam com frequência, então os números abaixo devem ser
reconferidos antes da execução se houver uma janela de tempo grande entre este RFC e o provisionamento.

### 4.1 New Relic

- **Plano gratuito permanente**, sem cartão de crédito: **100 GB de ingestão/mês** e um usuário
  "full platform" (acesso completo), mais usuários "basic" ilimitados para dashboards e alertas —
  [newrelic.com/pricing/free-tier](https://newrelic.com/pricing/free-tier). Acima de 100 GB, cobrança
  por GB adicional (não é um limite rígido que interrompe a ingestão, é billing incremental) —
  [Last9, "New Relic Pricing 2026"](https://last9.io/blog/new-relic-pricing/).
- **OTLP nativo**, sem SDK/agente proprietário exigido para APM: endpoint
  `https://otlp.nr-data.net` (portas 443/4317/4318), autenticado por um único header
  `api-key: <license key>` — exatamente o formato que `OTEL_EXPORTER_OTLP_HEADERS` já suporta
  ([docs.newrelic.com, "New Relic OTLP endpoint"](https://docs.newrelic.com/docs/opentelemetry/best-practices/opentelemetry-otlp/)).
  Traces têm suporte OTLP em GA; métricas e logs também são aceitos por OTLP, com ressalvas de
  maturidade em partes do onboarding automático — o suficiente para os instrumentos usados aqui
  (histogram, counter), mas vale conferir a documentação do tipo específico de métrica na hora da
  configuração.
- **CPU/memória do cluster:** a instrumentação da aplicação (`AddRuntimeInstrumentation`) só cobre
  o processo da API, não o cluster inteiro. Para node/pod-level, é preciso o Kubernetes
  integration da New Relic (`nri-bundle`, ver seção 6) — um componente adicional, não algo que o
  OTLP da aplicação já cobre.
- **Esforço de dashboard:** o New Relic constrói automaticamente visões de APM (latência, taxa de
  erro, throughput) a partir do span de entrada do ASP.NET Core assim que os traces chegam — sem
  configurar nada além de instalar o agente de Kubernetes. As métricas de negócio (`oficina.*`)
  aparecem como métricas customizadas navegáveis por NRQL, mas os **dashboards específicos** (volume
  diário de OS, tempo médio por etapa) ainda precisam ser montados manualmente com NRQL — nenhuma
  ferramenta descobre sozinha o que "tempo médio por etapa" significa para este domínio.

### 4.2 Datadog

- **Não há um plano gratuito permanente equivalente ao da New Relic para o que este projeto
  precisa.** O free tier do Datadog é só de *Infrastructure Monitoring*, limitado a **5 hosts e
  retenção de métricas de 1 dia** — [datadoghq.com/pricing](https://www.datadoghq.com/pricing/).
  **APM (que cobre os traces e a latência de API exigidos) não está incluído no free tier**: é um
  produto pago à parte, US$ 31/host/mês
  ([SigNoz, "Datadog Pricing Breakdown 2026"](https://signoz.io/blog/datadog-pricing/)). O caminho
  gratuito real do Datadog para este projeto é um **trial**, e a spec do próprio projeto já havia
  identificado esse trial como de ~14 dias em análises anteriores — prazo curto perto do intervalo
  entre gravar o vídeo e a correção acontecer.
- **OTLP:** o Datadog Agent tem um receptor OTLP embutido, então tecnicamente aceita o exportador
  já escrito sem mudança de código — mas o agente continua sendo necessário como DaemonSet (não é
  "sem agente"), e o produto que consome esses traces (APM) é o que está fora do free tier.
- **Esforço de dashboard:** comparável ao New Relic — boas visões prontas de infraestrutura, mas
  dashboards de negócio custom exigem configuração manual (com a consequência adicional de exigir
  o produto pago para ter os traces por trás delas).
- **Avaliação:** tecnicamente competente e com bom suporte a OTLP, mas **descartado neste projeto
  pelo critério decisivo da seção 2** — o gratuito real não cobre o requisito de traces/APM, e a
  alternativa paga não é sustentável para um trabalho acadêmico com conta AWS Academy sem
  orçamento (RFC-002, seção 2).

### 4.3 Auto-hospedado — Prometheus + Grafana + Tempo/Jaeger no próprio EKS

- **Custo:** zero em licenciamento — todo o custo é a computação que os componentes consomem
  dentro do próprio cluster (`kube-prometheus-stack`, Tempo ou Jaeger, e Loki se logs também forem
  centralizados) — [prometheus-community/helm-charts](https://github.com/prometheus-community/helm-charts/tree/main/charts/kube-prometheus-stack).
  Sem trial, sem expiração — mas também sem "plano gratuito" a comparar, porque não é um serviço de
  terceiro.
- **OTLP:** nativo — Tempo e o OTel Collector aceitam OTLP diretamente, sem adaptação.
- **Esforço:** o mais alto das três opções. Requer instalar e operar múltiplos componentes
  (Prometheus, Grafana, Alertmanager, Tempo/Jaeger, opcionalmente Loki), configurar armazenamento
  persistente para métricas/traces, e montar **todos** os dashboards e alertas do zero em
  PromQL/TraceQL — nada vem pronto. E consome parte da cota já apertada da conta AWS Academy
  (RFC-002, seção 6.2: instâncias até `large`, 32 vCPU e 9 instâncias por região) — cada pod do
  stack de observabilidade compete por essa cota com a própria aplicação, justo no ambiente mais
  restrito das três alternativas.
- **Avaliação:** correto e sem risco de expiração, mas o esforço de operação e a pressão sobre uma
  cota de cluster já limitada não se pagam para uma demonstração pontual de ~15 minutos de vídeo.
  Seria a escolha natural se o projeto fosse rodar em produção continuamente e por muito tempo, o
  que não é o caso aqui (RFC-002, seção 6.3).

### 4.4 Alternativa descartada mais cedo — Grafana Cloud

Registrada por transparência, ainda que fora da dupla sugerida pelo enunciado (Datadog/New Relic):
plano gratuito permanente e generoso (10 mil séries de métricas, 50 GB de logs e 50 GB de traces
por mês, sem cartão de crédito) — [Grafana Cloud, via CloudZero](https://www.cloudzero.com/blog/grafana-cloud-pricing/) —
com um único endpoint OTLP para os três sinais. A retenção de 14 dias no plano gratuito é suficiente
para o ciclo deste projeto. Seria uma alternativa honesta a New Relic no mesmo patamar de custo. Não
foi escolhida porque, assim como o Prometheus/Grafana/Tempo próprio, os dashboards de negócio e os
alertas ainda são montados manualmente (PromQL/LogQL/TraceQL) — sem o ganho de setup automático de
APM que o New Relic oferece — e o enunciado já indicava Datadog/New Relic como as opções esperadas.
Se o esforço manual de dashboard não for um problema para o grupo, é a alternativa mais defensável
depois da recomendação da seção 5.

## 5. Decisão

**New Relic.**

O critério decisivo é o combinado dos itens 2 e 4: é o único, entre as opções pagas orientadas pelo
enunciado (Datadog/New Relic), com **plano gratuito permanente que cobre o requisito completo**
(traces, métricas, logs, dashboards, alertas) sem depender de um trial com prazo de expiração que
pode não sobreviver ao intervalo entre a gravação do vídeo e a correção. O Datadog atende
tecnicamente ao mesmo contrato de instrumentação (OTLP), mas seu free tier deixa de fora justamente
o produto (APM) que cobre o requisito de traces e latência — tornando-o, na prática, uma opção paga
para este projeto.

Entre New Relic e a alternativa auto-hospedada, o esforço de configuração e a pressão sobre a cota
já restrita da conta AWS Academy Learner Lab (RFC-002) desempatam a favor do New Relic: dashboards
de APM e Kubernetes vêm prontos a partir da instrumentação já existente, sobrando esforço apenas
para os três dashboards de negócio específicos deste domínio (que qualquer ferramenta exigiria
montar manualmente).

Nada nesta decisão exige revisitar `ObservabilityExtensions.cs` ou `TraceJsonConsoleFormatter.cs`
— ela consome a instrumentação como está, pelo contrato descrito na seção 3.

## 6. O que fica pronto para a execução

### 6.1 Como o agente entra no cluster

Dois componentes distintos, com propósitos diferentes — não confundir um pelo outro:

1. **A aplicação já exporta OTLP diretamente** (traces + métricas de negócio + runtime do
   processo), sem precisar de um agente/sidecar — só o endpoint precisa apontar para o New Relic
   em vez do Jaeger local.
2. **Métricas de cluster (CPU/memória por node e pod, eventos do Kubernetes)** exigem o
   **Kubernetes integration da New Relic**, instalado via o chart **`nri-bundle`**
   ([newrelic/helm-charts](https://github.com/newrelic/helm-charts/tree/master/charts/nri-bundle)),
   que sobe como **DaemonSet** (um pod por node) mais os componentes de infraestrutura/eventos do
   cluster. Isso cobre o item "CPU e memória do Kubernetes" que a instrumentação da aplicação, por
   si só, não cobre — ela vê o próprio processo, não o node inteiro nem os demais pods.

Checklist de instalação:

```bash
helm repo add newrelic https://helm-charts.newrelic.com
helm upgrade --install newrelic-bundle newrelic/nri-bundle \
  --namespace newrelic --create-namespace \
  --set global.licenseKey=<licenseKey do Secret, nunca em texto plano> \
  --set global.cluster=oficina-mecanica \
  --set newrelic-infrastructure.privileged=true \
  --set kube-state-metrics.enabled=true
```

### 6.2 Como a chave de API chega sem ser commitada

O padrão já usado no projeto para segredos (RFC-002, seção 4: Secrets Manager como fonte;
Kubernetes `Secret` como destino no cluster) se aplica sem alteração:

1. A **license key da New Relic** é criada manualmente na conta (não é provisionável por
   Terraform, é um dado de conta de terceiro) e armazenada no **AWS Secrets Manager**, junto aos
   demais segredos da aplicação (segredo do JWT, connection string do RDS).
2. Um mecanismo já previsto para os demais segredos do cluster (External Secrets Operator ou o
   Secrets Manager CSI driver — ver `docs/planos/fase-3/00-analise-da-spec.md`, seção 3.2-C) projeta
   esse valor como um `Secret` do Kubernetes.
3. Duas variáveis de ambiente da aplicação lêem esse `Secret`:
   - `OTEL_EXPORTER_OTLP_ENDPOINT=https://otlp.nr-data.net`
   - `OTEL_EXPORTER_OTLP_HEADERS=api-key=<licenseKey>` — via `secretKeyRef`, nunca no `ConfigMap`.
4. O chart `nri-bundle` (seção 6.1) usa a **mesma license key**, passada como valor Helm que também
   deve vir de um `Secret` (`--set-file` ou `valuesFrom` apontando para o segredo montado), não como
   literal no `values.yaml` versionado.

Isso é uma continuação direta do que já vale hoje para os demais segredos da aplicação: não existe
mais um `k8s/base/02-secret.yaml` commitado. O Secret `oficina-secrets` é criado pela própria
pipeline (`.github/workflows/ci-cd.yml`, job "Deploy no EKS"), que lê os segredos do AWS Secrets
Manager (`oficina-mecanica/dev/rds/postgresql` e `oficina-mecanica/dev/app`) por nome, aplica
`::add-mask::` em cada valor e monta o `Secret` em runtime com `kubectl create secret generic
oficina-secrets ... --dry-run=client -o yaml | kubectl apply -f -`. A license key da New Relic
seguiria o mesmo caminho (via `OTEL_EXPORTER_OTLP_HEADERS`, passo 3 acima): nenhuma chave de API
fica literal em YAML versionado.

> **Caminho mais simples, provavelmente preferível.** O passo 2 acima assume o External Secrets
> Operator ou o Secrets Manager CSI driver — e ambos normalmente exigem **IRSA**, que exige criar um
> IAM role, coisa que esta conta **não permite** ([RFC-002](002-escolha-do-provedor-de-nuvem.md) §6.1
> e [ADR-006](../adrs/006-credenciais-de-nuvem-no-cicd.md)). Verifique isso antes de adotá-lo.
>
> Para **esta chave especificamente**, o rodeio nem se justifica: a license key da New Relic é um
> dado de terceiro, não um recurso da AWS. Ela pode ir de um **secret do GitHub direto para um
> `Secret` do Kubernetes**, criado pela pipeline no momento do deploy — sem passar pelo Secrets
> Manager, sem operador adicional e sem depender de IRSA. O Secrets Manager continua sendo a fonte
> certa para o que **é** da AWS (connection string do RDS, segredo do JWT), onde a Lambda também
> precisa ler o mesmo valor.

### 6.3 Dashboards e alertas — checklist mapeado à métrica/sinal

| Dashboard/alerta exigido | Sinal que alimenta | Fonte |
|---|---|---|
| Latência das APIs | Span de entrada do ASP.NET Core (duração, taxa de erro, throughput) | `AddAspNetCoreInstrumentation()` (traces), APM automático do New Relic |
| CPU/memória do Kubernetes | Métricas de node/pod do `nri-bundle` (kube-state-metrics + infra agent) | Kubernetes integration (seção 6.1), **não** vem do OTLP da aplicação |
| CPU/memória do processo .NET (GC, threads, heap) | Runtime instrumentation | `AddRuntimeInstrumentation()` (métricas) |
| Healthcheck e disponibilidade | Resultado de `/healthz`, `/healthz/live`, `/healthz/ready` | Uptime/synthetic check do New Relic apontando para os endpoints já existentes (README, seção Health checks) |
| Volume diário de OS (por tipo de abertura) | `oficina.ordens_servico.abertas` | `metricas.md` — Counter, tag `tipo` |
| Tempo médio por etapa (diagnóstico/execução/finalização) | `oficina.ordens_servico.etapa.duracao` | `metricas.md` — Histogram, tag `etapa` |
| Erros e falhas nas integrações | `oficina.integracoes.falhas` | `metricas.md` — Counter, tags `evento`/`handler` |
| **Alerta:** falha no processamento de ordens de serviço | Taxa de subida de `oficina.integracoes.falhas` | `metricas.md`, seção do instrumento — "serve de base para o alerta" |
| **Alerta:** healthcheck/disponibilidade caindo | Uptime check acima | Configuração de alerta associada ao mesmo synthetic check |
| Logs estruturados correlacionados | Linha JSON com `trace_id`/`span_id` | `TraceJsonConsoleFormatter` (ADR-004) — ingestão de log do New Relic lendo `stdout` do container |
| Traces em execução | Spans OTLP ponta a ponta (API → Npgsql) | `AddOtlpExporter()` em `WithTracing` |

Este checklist é o que vira tarefa de configuração na conta do New Relic quando a infraestrutura
estiver de pé — nenhum item aqui depende de código novo na aplicação.

## 7. Consequências

**Positivas**
- Nenhuma linha de `ObservabilityExtensions.cs` ou `TraceJsonConsoleFormatter.cs` muda por causa
  desta decisão — o argumento da seção 3 se confirma na prática.
- O plano gratuito permanente remove o risco de a evidência de observabilidade (dashboards, vídeo)
  parar de existir entre a gravação e a correção.
- Dashboards de APM e infraestrutura vêm prontos a partir da instrumentação já existente; o esforço
  manual fica restrito aos três dashboards de negócio, que qualquer ferramenta exigiria construir à
  mão.

**Negativas e mitigações**
- *A CPU/memória do cluster não vem "de graça" do OTLP da aplicação.* Mitigado documentando
  explicitamente (seção 6.1) que o `nri-bundle` é um componente adicional e obrigatório, não uma
  opção.
- *Acima de 100 GB/mês a ingestão passa a ser cobrada.* Baixo risco para o volume deste projeto
  (uma aplicação em Kubernetes com poucos pods, ambiente efêmero), mas deve ser monitorado se a
  amostragem 100% do ADR-004 gerar mais volume que o esperado durante os testes de carga, se
  houver.
- *A pesquisa de preços está sujeita a mudar entre este RFC e a execução.* Os valores e limites
  citados na seção 4 têm data e fonte registradas; reconferir antes de provisionar se o intervalo
  for grande.

## 8. Questões em aberto

- O plano descrito aqui não provisiona nada — a instalação do `nri-bundle`, a criação da conta e da
  license key, e a configuração dos dashboards/alertas do checklist (seção 6.3) ficam para a fase
  de execução (infraestrutura/CI-CD), fora do escopo deste RFC.
- Este RFC não altera o ADR-004; a recomendação da equipe é registrar, em um ADR futuro (ou como
  observação no PR desta mudança), que a amostragem 100% de traces vale enquanto o volume for baixo
  o suficiente para caber no plano gratuito — um ponto que o ADR-004 já sinaliza como "não seria
  adequado em produção de alto tráfego", mas que agora ganha um limite concreto (100 GB/mês).
