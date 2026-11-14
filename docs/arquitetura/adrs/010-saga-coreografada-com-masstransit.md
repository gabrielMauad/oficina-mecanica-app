# ADR-010 — Saga coreografada com MassTransit sobre SNS/SQS

- **Status:** Aceita
- **Data:** 2026-11-14
- **Substitui:** [ADR-007](007-padrao-de-comunicacao-entre-modulos.md) quanto ao mecanismo de
  comunicação assíncrona (o barramento em memória). O princípio de comunicação síncrona por _ports_
  e _adapters_ continua valendo; só os adapters mudam de _chamada em processo_ para _HTTP_.
- **Desenho do fluxo:** [`diagramas/saga.md`](../diagramas/saga.md)

## Contexto

Com um banco por serviço, o fluxo da OS (abertura → orçamento → aprovação → pagamento → execução)
não cabe numa transação. O enunciado exige uma **Saga** — orquestrada ou coreografada — com
**compensação**, mensageria assíncrona obrigatória (REST complementa, não substitui) e a escolha
justificada. A Fase 3 já tinha integration events publicados depois do commit
(`IIntegrationEventBus`, `IIntegrationEventHandler<T>`), com um barramento em memória.

## Decisão

**Saga coreografada.** Cada serviço publica um evento quando termina a sua etapa e reage aos eventos
dos outros; não existe coordenador. Os eventos, quem publica e quem consome estão na tabela de
[`diagramas/saga.md`](../diagramas/saga.md#eventos-de-integração).

- **Por que coreografia:**
  - o padrão de integration events **já existia** — a Saga estende o que o código fazia, em vez de
    introduzir um componente novo;
  - são **poucos participantes** (três serviços) e o fluxo é **linear**, com poucos desvios;
  - **não há ponto central de falha**: com um orquestrador, a indisponibilidade dele pararia todas as
    OS em andamento;
  - cada compensação é local ao serviço que conhece o estado a desfazer (o PecasInsumos estorna o
    próprio estoque; a OS rejeita o próprio orçamento).
- **Biblioteca:** **MassTransit 8.x** (licença aberta; a série 9 é comercial). Propaga o `traceparent`
  W3C nas mensagens, o que mantém um único trace por fluxo entre serviços (adicionando a fonte
  `MassTransit` ao tracer do OpenTelemetry).
- **Transporte:** **Amazon SNS + SQS** — serviços gerenciados e disponíveis na conta AWS Academy, sem
  broker rodando no cluster. O MassTransit cria tópicos, filas e assinaturas ao subir cada serviço,
  então não há Terraform de filas. O transporte é escolhido por configuração
  (`Mensageria__Transporte`): `AmazonSqs` na AWS e no LocalStack, `InMemory` nos testes.
- **Clean Architecture:** a Application continua publicando por `IIntegrationEventBus` e tratando por
  `IIntegrationEventHandler<T>`; o adapter MassTransit e os consumers ficam na camada externa e
  apenas delegam ao handler da Application.
- **Falhas técnicas:** retry do consumo (3 reentregas, 1 s / 5 s / 10 s) antes de desistir; **não** é
  compensação.
- **Compensação:** pagamento recusado → orçamento rejeitado → estorno; rejeição pelo cliente →
  estorno; falha na baixa de estoque → orçamento rejeitado sem estorno (a baixa é atômica, então
  nada foi baixado).
- **Consumidores toleram reentrega:** se o estado já reflete o evento, registram log e retornam.

## Alternativas consideradas

- **Orquestração** (um serviço que comanda as etapas): descartada. Para ser uma orquestração de
  verdade o coordenador teria de controlar todas as etapas, acompanhar o estado da Saga e tratar
  falhas e compensações — um sexto componente com estado próprio e ponto central de falha, para um
  fluxo de três participantes.
- **RabbitMQ no cluster:** viável, mas consome capacidade dos nós do EKS e exigiria operar o broker;
  fica como alternativa caso o SNS/SQS deixe de estar disponível — a troca se restringe ao adapter
  de transporte.
- **Outbox transacional e DLQ gerenciada:** fora do escopo; registrados como evolução futura em
  [`saga.md`](../diagramas/saga.md#limitações-conhecidas).

## Consequências

- (+) Nenhum componente novo além do broker gerenciado; cada serviço continua autônomo.
- (+) Um trace distribuído cobre o fluxo inteiro (HTTP, publicação e consumo).
- (−) O fluxo fica distribuído: entender uma OS exige seguir eventos entre serviços. Mitigação: o
  diagrama da Saga e o rastreamento distribuído no New Relic.
- (−) Sem outbox, publicar depois do commit não é atômico com a gravação; um evento pode se perder
  se a publicação falhar logo após o commit.
