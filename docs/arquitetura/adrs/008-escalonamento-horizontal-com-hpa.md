# ADR-008 — Escalonamento horizontal com HPA

- **Status:** Aceita
- **Data:** 2026-09-15
- **Contexto maior:** [RFC-002 — Escolha do provedor de nuvem](../rfcs/002-escolha-do-provedor-de-nuvem.md)

## Contexto

A Fase 3 exige um "cluster Kubernetes gerenciado com escalabilidade" (ver
[`diagramas/infraestrutura.md`](../diagramas/infraestrutura.md)) — a demonstração em vídeo precisa
mostrar a aplicação escalando sob carga, não só rodando em réplica fixa.

Duas restrições da conta **AWS Academy Learner Lab** moldam o espaço de escolha:

- **Não é possível criar IAM roles.** A conta só permite usar a role pré-criada `LabRole`
  ([RFC-002](../rfcs/002-escolha-do-provedor-de-nuvem.md), seção 6.1;
  [ADR-006](006-credenciais-de-nuvem-no-cicd.md)). Qualquer mecanismo que exija criar uma policy
  ou role IAM dedicada (para chamar APIs de EC2/Auto Scaling em nome do cluster, por exemplo) está
  fechado.
- **Limite de dimensionamento da conta:** instâncias até `large`, no máximo 32 vCPU e 9 instâncias
  simultâneas por região (RFC-002, seção 6.2). O repositório `oficina-mecanica-infra-k8s`
  provisiona o node group do EKS com instâncias `t3.small`, `desired_size = 2`, `min_size = 2`,
  `max_size = 3` (`variables.tf`) — um teto fixo de nós, não elástico.

Dentro desse node group, o Deployment da aplicação (`k8s/app/20-api-deployment.yaml`) já declara
`resources.requests` (100m CPU / 256Mi memória) e `resources.limits` (500m CPU / 512Mi memória)
por réplica — pré-requisito para o HPA baseado em CPU calcular `averageUtilization` (sem
`requests`, não há denominador para a porcentagem). O add-on `metrics-server` é provisionado pelo
`oficina-mecanica-infra-k8s` como **add-on gerenciado do EKS** (`aws_eks_addon`, `eks.tf`) — não
via Helm — justamente para alimentar o pipeline de métricas que o HPA consulta
(`README` daquele repositório, "Decisões de desenho": "é pré-requisito do HPA, escalabilidade é
requisito da fase").

## Decisão

Escalonamento horizontal de **pods** (não de nós) via `HorizontalPodAutoscaler` nativo do
Kubernetes, `autoscaling/v2`, definido em `k8s/app/22-api-hpa.yaml`:

- `scaleTargetRef`: Deployment `oficina-api`.
- `minReplicas: 1`, `maxReplicas: 5`.
- Métrica: `Resource` / `cpu`, tipo `Utilization`, `averageUtilization: 50`.

O número de **nós** do cluster permanece fixo, dimensionado no Terraform de
`oficina-mecanica-infra-k8s` (2 a 3 `t3.small`) — só a contagem de réplicas do pod da aplicação
escala em resposta à carga.

## Alternativas consideradas

**Réplicas fixas / escalonamento manual.** Mais simples de configurar, mas não atende ao
requisito explícito de escalabilidade da fase — não haveria nada para demonstrar em vídeo além de
um número fixo de pods. Descartada.

**Cluster Autoscaler ou Karpenter (escalar nós, não só pods).** Ambos precisam de uma role/policy
IAM dedicada para chamar as APIs de EC2/Auto Scaling Groups em nome do cluster (tipicamente via
IRSA) — algo que esta conta **não permite provisionar** (só `LabRole` pré-criada, sem criação de
roles novas — RFC-002 §6.1, ADR-006). Mesmo contornando isso, o node group já está no teto
dimensionado pela conta (`t3.small`, até 3 nós — `variables.tf` do
`oficina-mecanica-infra-k8s`), então escalar nós automaticamente teria pouco espaço para operar
antes de esbarrar no limite de 9 instâncias/32 vCPU da conta (RFC-002 §6.2). O requisito da fase é
escalar a aplicação sob carga, não a infraestrutura subjacente — o HPA de pods já cobre isso.
Descartada.

**VPA (Vertical Pod Autoscaler).** Ajusta `requests`/`limits` do próprio pod em vez do número de
réplicas, o que exige recriar o pod a cada ajuste — não serve para absorver um pico de tráfego em
tempo real como o HPA horizontal, e não há uso nem menção a VPA em nenhum manifesto deste
repositório. Descartada por não atender ao cenário de demonstração (pico de requisições).

**Escalonamento por métrica customizada (ex.: fila, latência via adaptador externo).** O chart
`nri-bundle` da New Relic inclui um adaptador de métricas para HPA
(`newrelic-k8s-metrics-adapter`), mas ele é **explicitamente desligado** em
`k8s/observabilidade/newrelic-values.yaml`, com a justificativa registrada no próprio arquivo: "o
HPA deste projeto usa métrica nativa de CPU (metrics-server, `k8s/app/22-api-hpa.yaml`), não
precisa de métrica externa". CPU via `metrics-server` já é suficiente e mais simples para o
cenário de demonstração exigido. Descartada.

## Consequências

**Positivas**
- `metrics-server` como add-on gerenciado do EKS evita configurar os providers
  `kubernetes`/`helm` do Terraform só para uma dependência do HPA (decisão espelhada e
  justificada no README de `oficina-mecanica-infra-k8s`).
- CPU é uma métrica simples de gerar e observar em uma demonstração pontual (gerar carga na API,
  ver o número de réplicas subir e descer).
- Nenhuma permissão de IAM adicional é necessária — o HPA e o `metrics-server` operam inteiramente
  dentro do que o cluster já expõe, compatível com a restrição de `LabRole` única.

**Negativas e mitigações**
- **O `maxReplicas: 5` do HPA não é livre — está limitado pela capacidade de pods do node group
  fixo.** `t3.small` suporta 11 pods por nó (limite de ENI/IP da instância, não CPU); com 2 nós
  são ~22 slots, dos quais ~6 já são consumidos por pods de sistema (kube-proxy, coredns,
  metrics-server, aws-node, mais o `nri-bundle` do New Relic), sobrando espaço suficiente para as
  5 réplicas da aplicação (ver README de `oficina-mecanica-infra-k8s`, "Decisões de desenho" e
  `k8s/observabilidade/newrelic-values.yaml`, conta de capacidade). Como não há Cluster
  Autoscaler/Karpenter (alternativa descartada acima), esse teto é fixo: se o HPA precisasse
  ultrapassar `maxReplicas: 5`, ou se a capacidade de pods do node group fosse o fator limitante
  antes disso, o próximo passo seria aumentar `node_group_max_size` ou o tipo de instância — uma
  mudança em `oficina-mecanica-infra-k8s`, não neste repositório.
- **O HPA baseado em CPU depende dos `resources.requests` do Deployment.** Sem
  `resources.requests.cpu: "100m"` (`k8s/app/20-api-deployment.yaml`), o `averageUtilization` não
  teria base de cálculo. Documentado aqui para que essa dependência não seja removida
  inadvertidamente numa limpeza de manifesto.
- **Não validado sob carga real.** O README de `oficina-mecanica-infra-k8s` ("O que não foi
  validado sem apply") registra que não foi confirmado, contra a conta real, se o HPA consegue de
  fato chegar a 5 réplicas sem esbarrar em memória antes de CPU nos nós `t3.small` — se isso
  acontecer, o degrau seguinte é trocar o tipo de instância do node group para `t3.medium`.
- **`minReplicas: 1` não dá alta disponibilidade por redundância em repouso.** Sem tráfego, a
  aplicação roda em uma única réplica — aceitável para o escopo de demonstração acadêmica deste
  projeto, não para um ambiente de produção real.
