# Validação com dados reais — New Relic (RFC-004)

Este documento registra a validação, feita contra a conta real de New Relic com tráfego real de
produção, dos nomes de evento/atributo usados em
[`dashboard-oficina-mecanica.json`](dashboard-oficina-mecanica.json) e
[`alertas.md`](alertas.md). É a evidência que sustenta a remoção dos `TODO: confirmar no primeiro
dado` que existiam nesses dois arquivos antes desta validação.

## Janela de tráfego gerado

**2026-09-15, 17:24:55Z – 17:32:34Z** — 88 chamadas feitas pela API Gateway, todas com resposta no
status HTTP esperado. Cenário: 6 ordens de serviço abertas (4 pela rota simples `POST
/api/v1/ordens-servico`, 2 pela rota completa `POST /api/v1/ordens-servico/completa`), das quais:

- 3 percorreram o ciclo completo até a entrega (diagnóstico → aprovação → execução → finalização).
- 1 teve o orçamento **rejeitado**, com estorno de estoque confirmado.

## Atributos confirmados por `keyset()`

**`Span`** (`service.name = 'oficina-mecanica-api'`): `span.kind`, `http.route`,
`http.request.method`, `http.response.status_code` (numérico), `url.path`, `duration.ms`
(numérico), `trace.id`, `transaction.name`, `name`, `service.name`, `db.system.name`,
`db.query.text`. **Não existem** `kind` nem `http.status_code` — os widgets e alertas que usavam
esses nomes foram corrigidos.

**`K8sContainerSample`** (`clusterName = 'oficina-mecanica'`): `cpuUsedCores`,
`memoryWorkingSetBytes`, `memoryUsedBytes`, `cpuLimitCores`, `memoryLimitBytes`,
`cpuRequestedCores`, `memoryRequestedBytes`, `podName`, `containerName`, `namespaceName`,
`nodeName`, `deploymentName`, `restartCount`. Os widgets de CPU e memória por pod (hoje "CPU do
cluster, por pod (cores)" e "Memória do cluster, por pod" — ver seção "Ajustes de visualização"
sobre a divisão em dois widgets) usavam `K8sPodSample`, que não é o event type que carrega esses
atributos na integração instalada neste cluster — foi trocado por `K8sContainerSample`, mantendo
os mesmos campos, filtro (`clusterName = 'oficina-mecanica' AND namespaceName = 'oficina-mecanica'`)
e `FACET podName`.

**`K8sNodeSample`**: o event type existe e recebe dados neste cluster, mas seus atributos não
foram (e não precisaram ser) enumerados por `keyset()` nesta rodada de validação. Os widgets "CPU
do cluster, por nó (cores)" e "Memória do cluster, por nó" (antes um único widget "CPU e memória
do cluster, por nó" — ver "Ajustes de visualização") mantêm `cpuUsedCores` e `memoryUsedBytes`,
que são os atributos documentados pela integração Kubernetes da New Relic para esse event type —
por isso o título **não** carrega mais um `TODO`, mesmo sem uma segunda confirmação por
`keyset()` especificamente para `K8sNodeSample`.

## Filtro de health check

Os widgets e alertas que consultam `Span` para latência e taxa de erro HTTP excluem
`url.path LIKE '/healthz%'`. Motivo: o NLB do EKS consulta `/healthz/live` a cada 10 segundos em
cada nó do cluster, e o kubelet também chama as mesmas rotas de liveness/readiness probe. Esse
tráfego sintético de infraestrutura é ortogonal ao tráfego real de negócio e, em volume, domina os
spans de servidor da API — sem o filtro, tanto a latência (p95/média) quanto a taxa de erro 5xx
ficariam distorcidas pelo comportamento das probes, e não refletiriam a experiência real de quem
chama a API.

## Métricas de negócio — esperado × medido

As consultas de métrica de negócio (`oficina.ordens_servico.abertas` com `FACET tipo` e
`oficina.ordens_servico.etapa.duracao` com `FACET etapa`) já estavam corretas e **não foram
alteradas**. A tabela abaixo é a evidência de que os valores batem com o cenário gerado.

| Métrica | Esperado | Medido |
|---|---|---|
| `abertas`, tipo `simples` | 4 | 4 |
| `abertas`, tipo `completa` | 2 | 2 |
| `etapa.duracao`, `execucao` | 44,0s (3 amostras: 46,2 / 37,0 / 48,9) | 44,2s |
| `etapa.duracao`, `finalizacao` | 49,2s (3 amostras: 51,1 / 35,9 / 60,7) | 49,1s |
| `etapa.duracao`, `diagnostico` | ver explicação abaixo | 30,3s |

### Explicação do diagnóstico

As duas OS do ciclo completo (aprovadas) tiveram diagnósticos de 48,6s e 41,3s. A **OS rejeitada**
também passou pela transição *iniciar diagnóstico → registrar diagnóstico*, praticamente sem
espera entre as duas chamadas, o que gera uma terceira amostra de aproximadamente 1,0s (o
orçamento foi rejeitado logo depois do diagnóstico, não antes dele). Três amostras com média
30,3s implicam uma terceira amostra de:

```
30,3 × 3 − 48,6 − 41,3 ≈ 1,0s
```

que bate exatamente com o esperado. Ou seja, a métrica mediu corretamente inclusive o caminho
alternativo (rejeição), e não só o caminho feliz. A OS aberta pela rota **completa** não gera
amostra de diagnóstico porque já nasce aguardando aprovação de orçamento — pula a etapa de
diagnóstico manual.

## Correlação log ↔ trace

Confirmada: logs de `service.name = 'oficina-mecanica-api'` chegam com `trace.id`, e o mesmo
`trace.id` localiza os spans da mesma requisição em `Span`. Procedimento reproduzível:

```sql
SELECT trace.id, message FROM Log WHERE service.name = 'oficina-mecanica-api' AND trace.id IS NOT NULL SINCE 2 hours ago LIMIT 5
```

```sql
SELECT name, span.kind, duration.ms FROM Span WHERE trace.id = '<trace.id>' SINCE 2 hours ago
```

Pegue um `trace.id` retornado pela primeira consulta e substitua na segunda para ver os spans
daquela requisição específica.

### Observação sobre respostas 403

Respostas **403** geradas pelo middleware de autorização do ASP.NET Core (usuário autenticado sem
o papel exigido) **não produzem log de aplicação**, porque `appsettings.json` filtra a categoria
`Microsoft.AspNetCore` abaixo de `Warning`, e a rejeição de autorização não sobe acima desse
nível. Por isso, a correlação log ↔ trace acima **não** deve ser demonstrada com uma chamada que
resulte em 403 — não há log para achar o `trace.id`. A demonstração usa uma requisição que
efetivamente passa pela aplicação e gera log, como a abertura de uma ordem de serviço.

## Widget de uptime

O widget "Uptime / healthcheck" consulta o event type `SyntheticCheck`, que só existe depois que o
monitor sintético `oficina-mecanica-healthz` for criado (roteiro em
[`uptime.md`](uptime.md)). O `TODO` foi removido do título porque a consulta em si está correta —
o widget passa a mostrar dado assim que o monitor existir; não depende de nenhuma correção de
nome de atributo.

## Ajustes de visualização

O dashboard foi importado (com `accountId` real) e testado contra os mesmos dados desta validação.
As **consultas** de todos os widgets conferiram — os três problemas abaixo eram só de
**visualização** (tipo de gráfico, eixo, período), e foram corrigidos sem tocar em nenhuma NRQL
que já estava certa.

### 1. "Tempo médio por etapa" parecia vazio

A consulta usava `TIMESERIES 1 hour` num gráfico de linha (`viz.line`). Como todo o tráfego de
teste caiu dentro de uma única hora, cada `FACET etapa` produzia **um único ponto** — e um gráfico
de linha não desenha linha com um ponto só, então a tela mostrava apenas marcas quase invisíveis
na borda. Correção: removido o `TIMESERIES` (a consulta virou um agregado simples por etapa) e a
visualização trocada para barras (`viz.bar`), com o título deixando explícito que o valor é em
segundos.

### 2. CPU invisível nos widgets de cluster

Os widgets "CPU e memória do cluster, por nó" e "…, por pod" colocavam CPU (em cores, ex.: `0,05`)
e memória (em bytes, ex.: `1,5 GB`) na mesma série/eixo. Na escala dos bytes, a linha de CPU fica
colada no zero e desaparece visualmente. Correção: cada widget foi dividido em dois, um por
métrica/unidade — "CPU do cluster, por nó (cores)", "Memória do cluster, por nó", "CPU do
cluster, por pod (cores)" e "Memória do cluster, por pod" — todos em `viz.line`, mesmas consultas
e filtros de antes, cada um agora com uma única métrica.

### 3. "Volume diário de OS" não aparecia como diário

O **período selecionado no dashboard** (ex.: "Since 3 hours ago") é menor que o balde de
`TIMESERIES 1 day` da consulta. Quando isso acontece, o New Relic ignora o balde pedido e escolhe
baldes automáticos de poucos minutos, produzindo picos de 1–2 em vez de uma barra por dia.
Correção: a consulta continua `TIMESERIES 1 day SINCE 7 days ago`, a visualização virou barras
empilhadas (`viz.stacked-bar`, uma barra por dia, simples + completa empilhados), e o widget
carrega uma `description` (tooltip, visível ao passar o mouse) avisando que **o período do
dashboard precisa ser de pelo menos alguns dias** (ex.: "Since 7 days ago") para os baldes diários
aparecerem.

### Identificadores de visualização usados

Confirmados na documentação oficial da New Relic e no código-fonte público do provider Terraform
mantido pela própria New Relic (que mapeia cada tipo de widget para o id usado no NerdGraph):

- `viz.line` e `viz.billboard` — exemplos de JSON em
  [Import, export, and add dashboards and charts](https://docs.newrelic.com/docs/query-your-data/explore-query-data/dashboards/dashboards-charts-import-export-data/).
- `viz.bar`, `viz.billboard`, `viz.table` (entre outros) — exemplos de JSON em
  [NerdGraph tutorial: Create and configure dashboard widgets](https://docs.newrelic.com/docs/apis/nerdgraph/examples/create-widgets-dashboards-api/).
- `viz.stacked-bar` — mapeamento `widget_stacked_bar` → `"viz.stacked-bar"` em
  [`structures_newrelic_one_dashboard.go`](https://github.com/newrelic/terraform-provider-newrelic/blob/main/newrelic/structures_newrelic_one_dashboard.go),
  no repositório oficial `newrelic/terraform-provider-newrelic`.

## Resumo das correções aplicadas

| Item | Antes | Depois |
|---|---|---|
| Tipo do span de servidor | `kind = 'server'` | `span.kind = 'server'` |
| Status HTTP do span | `numeric(http.status_code) >= 500` | `http.response.status_code >= 500` (já numérico) |
| Agrupamento de rota | `FACET name` | `FACET http.route` |
| Exclusão de health check | (ausente) | `AND url.path NOT LIKE '/healthz%'` |
| Métricas de container Kubernetes | `K8sPodSample` | `K8sContainerSample` |
| Filtro de rotas de OS em alertas | `name LIKE '%ordens-servico%'` | `http.route LIKE '%ordens-servico%'` |
