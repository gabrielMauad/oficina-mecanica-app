# Monitor sintético (uptime) — New Relic

> ⚠️ **A URL do API Gateway muda a cada recriação da infraestrutura** (RFC-002, seção 6.3: a
> infra é destruída e recriada, e o novo `oficina-mecanica-api` do API Gateway ganha um endpoint
> novo). O monitor **não se atualiza sozinho** — antes de qualquer gravação de vídeo ou nova
> demonstração, **edite a URL do monitor** com o passo 2 abaixo, ou o monitor vai ficar batendo
> num endpoint morto e reportando "down" incorretamente.

## 1. Descobrir a URL atual do API Gateway

Comando de leitura (`describe`/`get`, permitido), roda com as credenciais da AWS Academy já em
`~/.aws/credentials`:

```bash
aws apigatewayv2 get-apis \
  --query "Items[?Name=='oficina-mecanica-api'].ApiEndpoint | [0]" \
  --output text
```

O endpoint de healthcheck completo é `<saída do comando acima>/healthz/ready`.

## 2. Criar (ou atualizar) o monitor

Na interface do New Relic: **Synthetic monitoring > Create monitor**.

- **Tipo:** "Ping" (simple browser também funciona, mas Ping é mais barato em ingestão e
  suficiente para validar status HTTP — não há necessidade de renderizar JS numa rota de health).
- **Nome:** `oficina-mecanica-healthz` (nome usado pela condição de alerta em `alertas.md` e pelo
  widget de uptime em `dashboard-oficina-mecanica.json` — mantenha esse nome exato se recriar o
  monitor do zero, ou atualize os dois documentos se escolher outro).
- **URL:** `https://<endpoint-do-passo-1>/healthz/ready`.
- **Frequência:** a cada 5 ou 10 minutos (suficiente para uma demonstração pontual; frequência
  maior só consome mais da cota de ingestão do plano gratuito sem ganho para este projeto).
- **Locations:** pelo menos uma localidade próxima da região da AWS usada (`us-east-1` — RFC-002),
  para não medir latência de rede intercontinental como se fosse latência da aplicação.
- **Advanced options > Response validation:** valide status HTTP `200`. O endpoint
  `/healthz/ready` já retorna um JSON com o resultado dos health checks configurados
  (README, seção "Health checks") — o status 200 sozinho já é suficiente para "disponível"; não é
  necessário validar o corpo da resposta.

## 3. Antes de cada gravação/demonstração

1. Rode o comando do passo 1 de novo — a infraestrutura pode ter sido recriada desde a última
   vez.
2. Se a URL mudou, edite o monitor (**Synthetic monitoring > oficina-mecanica-healthz > Edit**) e
   atualize a URL.
3. Force uma execução manual (**Run now**, se disponível na interface) para confirmar `200` antes
   de começar a gravar — evita descobrir um monitor "down" só depois, com o vídeo já gravado.

## 4. Onde isso aparece depois de configurado

- Widget "Uptime / healthcheck" em `dashboard-oficina-mecanica.json` (evento `SyntheticCheck`,
  filtrado por `monitorName = 'oficina-mecanica-healthz'`).
- Condição de alerta "Healthcheck/disponibilidade" em `alertas.md`, associada diretamente a este
  monitor (tipo "Synthetic monitor", não NRQL).
