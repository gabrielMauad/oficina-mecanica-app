# ADR-007 — Padrão de comunicação entre módulos

- **Status:** Aceita
- **Data:** 2026-09-15

## Contexto

O sistema é um _Modular Monolith_ com quatro Bounded Contexts (Cadastro, OrdensServico,
PecasInsumos, Autenticacao) separados por fronteiras **físicas** — projeto próprio por camada por
módulo, não camadas horizontais (ver
[`diagramas/componentes.md`](../diagramas/componentes.md), Nível 2). A regra que sustenta esse
isolamento é simples de enunciar e fácil de violar sem um padrão explícito: **nenhum módulo
referencia diretamente Domain ou Application de outro módulo**
([`decisoes.md`](../decisoes.md#comunicação-entre-módulos)). Cada módulo também tem seu próprio
schema de banco, sem FK entre schemas (ver
[ADR-002](002-lambda-le-o-banco-diretamente.md)), o que fecha a porta ao atalho mais óbvio —
acessar a tabela do outro módulo direto.

Ainda assim, os módulos precisam colaborar em dois cenários recorrentes:

- **Precisa de uma resposta imediata para decidir algo agora** — ex.: ao abrir uma OS, o módulo
  OrdensServico precisa saber se o cliente informado existe e está ativo (dado que pertence ao
  módulo Cadastro).
- **Reage a um fato já consumado, sem bloquear quem o gerou** — ex.: quando um orçamento é gerado
  (evento de domínio do módulo OrdensServico), o módulo PecasInsumos precisa decrementar o
  estoque reservado.

Sem um padrão único para os dois casos, cada módulo inventaria sua própria forma de "pedir
emprestado" um dado ou reagir a um fato de outro módulo, e a fronteira física do Nível 2 viraria
decorativa.

## Decisão

Dois mecanismos, um para cada cenário, ambos **dentro do mesmo processo** (o monólito roda em um
único container):

**1. Síncrona, via ACL (anti-corruption layer):** o módulo consumidor define uma _port_ na sua
própria camada Application, no seu próprio vocabulário (ex.:
`OrdensServico.Application.Gateways.IClienteGateway`), e implementa um _adapter_ na camada
Adapters que satisfaz essa port chamando os `Contracts` publicados pelo módulo produtor. O
adapter faz a tradução entre os dois vocabulários — o consumidor nunca enxerga o tipo do
produtor. Exemplo real:
`OrdensServico.Adapters.Gateways.ClienteGateway` implementa `IClienteGateway` chamando
`Cadastro.Contracts.Queries.ICadastroClienteQuery.ObterPorId`, e traduz o DTO do Cadastro para o
`ClienteInfo` que o Domain de OrdensServico entende.

**2. Assíncrona, via Integration Events in-process:** o módulo produtor publica um evento de
integração (definido em `<Modulo>.Contracts/IntegrationEvents`, ex.:
`OrcamentoGeradoIntegrationEvent`) através do `IIntegrationEventBus`, sempre **depois do commit**
— é o último passo do `TransactionBehavior`
([`decisoes.md`](../decisoes.md#transactionbehavior--orquestração-pós-commit)). Módulos
consumidores registram implementações de `IIntegrationEventHandler<T>` (ex.:
`PecasInsumos.Application.IntegrationEventHandlers.DecrementarEstoqueQuandoOrcamentoGerado`), sem
o produtor conhecer quem consome. A implementação atual do bus é a
`SharedKernel.Application.InMemoryIntegrationEventBus`: resolve os handlers via
`IServiceProvider` e os invoca diretamente, no mesmo processo — **não existe broker externo**
(SQS, RabbitMQ ou equivalente) neste projeto.

Em ambos os casos, `Contracts` é o único pacote que os dois lados podem enxergar um do outro — a
"linguagem publicada" do módulo produtor (ver [`diagramas/componentes.md`](../diagramas/componentes.md),
Nível 3, "Por que Contracts é um pacote à parte").

## Alternativas consideradas

**Acesso direto a repositórios/tabelas de outro módulo.** Seria o caminho de menor esforço
(nenhuma port, nenhum adapter, nenhum contrato). Descartada porque contradiz diretamente o
isolamento por schema já decidido (ADR-002: "um schema por módulo e nenhuma FK entre schemas") —
aceitar esse atalho em qualquer módulo além da exceção documentada da Lambda tornaria a fronteira
física do Nível 2 apenas nominal, e qualquer refatoração de schema de um módulo quebraria outros
módulos sem aviso em tempo de compilação.

**Broker de mensageria externo (ex.: SQS, RabbitMQ).** Desacoplaria produtor e consumidor no
tempo (fila com persistência, reprocessamento, DLQ) e permitiria escalar/depurar módulos como
processos independentes no futuro. Descartada nesta fase: o sistema é um monólito modular que
roda em um único container/processo — não há hoje unidade de deploy separada por módulo que
justifique a complexidade operacional de um broker (fila gerenciada ou cluster próprio, mais um
componente a provisionar na já restrita conta AWS Academy Learner Lab). Também não resolveria
nada que o `IServiceProvider` já não resolva dentro do mesmo processo, e adicionaria uma rede como
novo modo de falha entre publicar e consumir. Fica registrada como o caminho natural **se e
quando** algum módulo for extraído para um serviço/deploy separado.

**Chamada HTTP entre módulos (mesmo dentro do mesmo processo).** Rejeitada por ser uma volta
desnecessária: os dois lados já compartilham o mesmo processo e o mesmo `IServiceProvider` — dar
a volta por um cliente HTTP/loopback só adicionaria serialização, desserialização e um timeout de
rede como novo modo de falha, sem nenhum ganho de isolamento que o padrão Contracts + ACL já não
entregue em tempo de compilação.

## Consequências

**Positivas**
- A Regra de Dependência da Clean Architecture (ver
  [`diagramas/componentes.md`](../diagramas/componentes.md), Nível 3) permanece verificável em
  tempo de compilação: nenhum módulo referencia Domain/Application de outro, só `Contracts`.
- O contrato entre módulos é explícito e teste-ável (interfaces + DTOs em `Contracts`), em vez de
  acoplamento implícito por convenção de nome de tabela.
- Falhas de integração são observáveis: `InMemoryIntegrationEventBus.Publish<T>` envolve cada
  handler em `try/catch` e incrementa o contador `oficina.integracoes.falhas` (tags `evento` e
  `handler`) antes de relançar a exceção — esse contador alimenta o dashboard de "erros e falhas
  nas integrações" e a condição de alerta "falha no processamento de ordens de serviço" no New
  Relic (ver [`metricas.md`](../metricas.md#oficinaintegracoesfalhas) e
  [RFC-004](../rfcs/004-ferramenta-de-observabilidade.md), seção 6.3).

**Negativas e mitigações**
- *A publicação do integration event acontece depois do commit.* Se um `IIntegrationEventHandler`
  lançar exceção, a escrita original do produtor **já foi persistida** — não há rollback
  automático, e handlers seguintes na mesma publicação não são executados
  ([`metricas.md`](../metricas.md#oficinaintegracoesfalhas)). Mitigado por observabilidade, não por
  transação: a falha vira um sinal (métrica + alerta), a ser tratado operacionalmente, não uma
  garantia de atomicidade entre módulos.
- *Sem broker, não há retentativa automática nem fila de reprocessamento (DLQ).* Uma falha de
  handler exige intervenção manual para reprocessar o efeito perdido (ex.: reprocessar o
  decremento de estoque). Aceitável no volume baixo deste projeto acadêmico; seria o primeiro
  ponto a revisitar num cenário de produção com volume real.
- *Acoplamento de deploy.* Como a comunicação é in-process, todos os módulos sobem juntos, no
  mesmo container — não há como escalar ou implantar um módulo isoladamente hoje. É a troca
  esperada de um monólito modular, revertida apenas se um módulo for extraído para um serviço
  próprio (ver alternativa do broker externo, acima).
