# Alertas — New Relic (RFC-004)

> As condições abaixo são criadas manualmente na interface do New Relic (Alerts & AI > Policies),
> porque a conta gratuita usada neste projeto não é provisionável por Terraform (RFC-004, seção
> 6.2). Este documento é o roteiro de criação, não um artefato importável como o dashboard.

## Antes de criar as condições

1. Crie uma **política de alerta** (Alert Policy) chamada `oficina-mecanica`, estratégia de
   agregação de incidentes "By condition and signal" (o padrão).
2. Crie o **destino de notificação** (e-mail) uma única vez, reaproveitado por todas as condições
   abaixo:
   - Alerts & AI > Destinations > **+ Add a destination** > Email.
   - Informe o(s) e-mail(s) do grupo que precisa ser avisado.
   - Em Alerts & AI > **Workflows**, crie um workflow associado à política `oficina-mecanica` e ao
     destino de e-mail criado — é o workflow, não o destino sozinho, que efetivamente dispara a
     notificação quando um incidente abre nessa política.

## Condição 1 — Falha no processamento de ordens de serviço

Exigência explícita da spec (RFC-004, seção 2). Cobre falhas de integração (contador de negócio) e
erros HTTP 5xx nas rotas de ordens de serviço — as duas faces de "processamento de OS falhando".

**Sinal A — falhas de integração:**

```sql
SELECT sum(`oficina.integracoes.falhas`) FROM Metric
```

- **Tipo de condição:** NRQL, "Static" threshold.
- **Limiar:** `above 0`, **at least once** in **5 minutos**.
- **Justificativa do limiar:** o instrumento (`metricas.md`) só incrementa quando um
  `IIntegrationEventHandler` lança exceção durante o processamento de um evento — não há falso
  positivo esperado em operação normal; qualquer valor acima de zero já é o sinal correto de
  "houve uma falha", não um agregado a suavizar. Janela curta (5 min) porque o volume de OS deste
  projeto é baixo — uma janela longa atrasaria a notificação sem ganho de sinal.

**Sinal B — 5xx nas rotas de ordens de serviço:**

```sql
SELECT percentage(count(*), WHERE http.response.status_code >= 500) AS 'erro %'
FROM Span
WHERE service.name = 'oficina-mecanica-api' AND span.kind = 'server'
  AND url.path NOT LIKE '/healthz%' AND http.route LIKE '%ordens-servico%'
```

> Atributos confirmados com dados reais (`keyset()` em `Span`, `service.name =
> 'oficina-mecanica-api'`): `http.response.status_code` (numérico) e `span.kind` — não existem
> `http.status_code` nem `kind`. O agrupamento/filtro de rota usa `http.route` (o template da
> rota, ex.: `api/v1/ordens-servico/{id}/status`, conforme
> `OrdemServicoApiController`, prefixo `api/v1/ordens-servico`), não `name`. O filtro
> `url.path NOT LIKE '/healthz%'` exclui os health checks batidos pela NLB (a cada 10s por nó) e
> pelo kubelet, que dominam o volume de spans de servidor e distorceriam a taxa de erro medida.
> Ver `docs/observabilidade/validacao.md` para o procedimento de validação com dados reais.

- **Tipo de condição:** NRQL, "Static" threshold.
- **Limiar:** `above 0`, **at least once** in **5 minutos**.
- **Justificativa:** qualquer 5xx nas rotas de ordens de serviço já é uma falha de processamento
  visível ao cliente da API — não há tolerância aceitável a suavizar com um limiar percentual
  maior no volume baixo deste projeto.

## Condição 2 — Healthcheck / disponibilidade caindo

```
Tipo de condição: "Synthetic monitor" (não NRQL) — associe diretamente o monitor sintético
criado em uptime.md (monitor "oficina-mecanica-healthz").
```

- **Gatilho:** falha do monitor (`Monitor failed`), **at least once** em uma execução.
- **Justificativa:** o monitor sintético já encapsula a definição de "disponível" (200 em
  `GET /healthz/ready` através do API Gateway, a cadeia inteira API Gateway → VPC Link → NLB →
  pod → RDS); alertar na primeira falha, sem exigir falhas consecutivas, porque um healthcheck
  indisponível durante a demonstração é o pior cenário a não perceber tarde.

## Condição 3 (opcional) — Latência alta

```sql
SELECT percentile(duration.ms, 95) FROM Span
WHERE service.name = 'oficina-mecanica-api' AND span.kind = 'server'
  AND url.path NOT LIKE '/healthz%'
```

> Mesmo atributo `duration.ms` do dashboard (`dashboard-oficina-mecanica.json`), confirmado com
> dados reais — ver `docs/observabilidade/validacao.md`. `span.kind` (não `kind`) e o filtro
> `url.path NOT LIKE '/healthz%'` excluem os health checks da NLB/kubelet, que senão dominariam a
> amostra e distorceriam o p95 de latência medido.

- **Limiar sugerido:** `above 2000` (2 segundos) por **5 minutos**, "at least once".
- **Justificativa do limiar:** não há SLA formal definido para este projeto acadêmico; 2s é um
  valor conservador acima do que se espera de operações CRUD simples com um banco RDS na mesma
  região, alto o suficiente para não disparar por variação normal de rede/cold start, baixo o
  suficiente para pegar uma regressão real. Ajuste depois de ver a latência real em produção
  (primeiro dado).
- **Por que é opcional:** não está na lista de exigências explícitas do RFC-004 (seção 2) — é uma
  boa prática de APM, não um requisito da spec. Inclua se sobrar tempo; não é bloqueador de
  entrega.

## Resumo

| Condição | Obrigatória? | Sinal | Limiar | Janela |
|---|---|---|---|---|
| Falha no processamento de OS (integrações) | Sim | `oficina.integracoes.falhas` | `> 0` | 5 min, at least once |
| Falha no processamento de OS (5xx) | Sim | Span 5xx em rotas de OS | `> 0` | 5 min, at least once |
| Healthcheck/disponibilidade | Sim | Monitor sintético (uptime.md) | Falha do monitor | 1ª falha |
| Latência alta | Opcional | `duration.ms` p95 | `> 2000ms` | 5 min, at least once |
