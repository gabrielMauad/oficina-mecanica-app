# Métricas de negócio

> Contrato de instrumentação criado na Fase 3, item "Dashboards" (volume diário de OS, tempo médio
> de execução por etapa, erros e falhas nas integrações) e "alertas para falhas no processamento de
> ordens de serviço". Ver `docs/planos/fase-3/00-analise-da-spec.md`, seções 1.4 e 3.2-B.

> **Fase 4 — métricas por serviço.** Cada serviço publica as suas métricas sob o próprio `service.name`
> (`oficina-mecanica-<serviço>`; ver [README](../../README.md#observabilidade-opentelemetry--new-relic)).
> Os instrumentos de **OS** (`oficina.ordens_servico.*`) vêm só do `ordens-servico`; o contador
> `oficina.integracoes.falhas` é emitido por quem **consome** eventos — `ordens-servico`,
> `pecas-insumos` e `pagamento`. Cadastro e Autenticacao registram o meter, mas não consomem eventos e
> por isso não emitem. O MassTransit não exporta métricas próprias neste projeto: o volume de eventos
> publicados e consumidos aparece nos **spans** (`<Evento> send`, `<fila> receive`/`process`) — ver
> [`docs/observabilidade/`](../observabilidade/).

A instrumentação usa apenas a API nativa do .NET, `System.Diagnostics.Metrics` (`Meter`,
`Counter<T>`, `Histogram<T>`) — **não depende do OpenTelemetry**. O OTel (ou o agente do
vendor de observabilidade escolhido) é responsável só por *coletar e exportar* essas métricas;
essa tarefa fica para a instrumentação de OTel feita em paralelo, que usa os nomes de `Meter`
abaixo para descobrir os instrumentos publicados pela aplicação.

## Meters

| Meter | Onde vive | Classe |
|---|---|---|
| `OficinaMecanica.OrdensServico` | `OrdensServico.Application` | `OrdensServico.Application.Metrics.OrdensServicoMetrics` |
| `OficinaMecanica.Integracoes` | `SharedKernel.Application` (cópia em cada serviço) | `SharedKernel.Application.Metrics.IntegracoesMetrics` |

Cada classe encapsula um único `Meter` singleton (registrado via `AddOrdensServicoModule` /
`AddSharedKernelServices`, seguindo o mesmo padrão de registro dos demais serviços do módulo) e o
descarta corretamente em `Dispose()` — o container de DI chama `Dispose()` automaticamente ao
encerrar a aplicação, por ser singleton.

## Instrumentos

### `oficina.ordens_servico.abertas`

- **Tipo:** `Counter<long>` · **Unidade:** `{ordem}`
- **Descrição:** "Ordens de serviço abertas"
- **Tag:** `tipo` = `simples` | `completa`
- **Alimenta:** volume diário de ordens de serviço no dashboard (soma por dia, quebrada por tipo
  de abertura).
- **Emitido em:**
  - `GerarOrdemServicoHandler` com `tipo=simples`, após `IOrdemServicoGateway.Adicionar` ter
    sucesso.
  - `AbrirOrdemServicoCompletaHandler` com `tipo=completa`, após `IOrdemServicoGateway.Adicionar`
    ter sucesso.
- Nada é emitido se o `Result` do handler for falha (cliente/veículo inválido, serviço ou peça
  indisponível, etc.) — o incremento só acontece depois que a OS já foi persistida.

### `oficina.ordens_servico.etapa.duracao`

- **Tipo:** `Histogram<double>` · **Unidade:** `s` (segundos)
- **Descrição:** "Duração de cada etapa da ordem de serviço"
- **Tag:** `etapa` = `diagnostico` | `execucao` | `finalizacao`
- **Alimenta:** tempo médio de execução por etapa (Diagnóstico / Execução / Finalização) no
  dashboard.
- **Emitido em:**

  | Etapa | Intervalo medido | Handler |
  |---|---|---|
  | `diagnostico` | de `EmDiagnostico` até o diagnóstico ser registrado | `RegistrarDiagnosticoHandler` |
  | `execucao` | de `EmExecucao` até `Finalizada` | `FinalizarOrdemServicoHandler` |
  | `finalizacao` | de `Finalizada` até `Entregue` | `ConcluirOrdemServicoHandler` |

  Em todos os três, nada é emitido se a transição de domínio (`RegistrarDiagnostico`,
  `Finalizar`, `Concluir`) retornar falha.

#### Como a duração é derivada — e a limitação que restou

A entidade `OrdemServico` (`OrdensServico.Domain`) guarda dois timestamps de auditoria/alteração
genéricos — `CriadoEm` e `AtualizadoEm` — e um terceiro, `StatusAlteradoEm`, dedicado a marcar
**quando a OS entrou no status em que está**. `AtualizadoEm` continua sendo reescrito com
`DateTime.UtcNow` a cada método de transição de estado (`IniciarDiagnostico`,
`RegistrarDiagnostico`, `EnviarOrcamento`, `AprovarOrcamento`, `RejeitarOrcamento`, `Executar`,
`Finalizar`, `NotificarCliente`, `Concluir`) — nada mudou nesse comportamento. `StatusAlteradoEm`,
por outro lado, é reescrito **exclusivamente** pelos métodos que de fato mudam `Status`
(`IniciarDiagnostico`, `EnviarOrcamento`, `Executar`, `Finalizar`, `Concluir`, e a construção via
`AbrirComServicos`) — os quatro métodos que só tocam `AtualizadoEm` sem mudar `Status`
(`RegistrarDiagnostico`, `AprovarOrcamento`, `RejeitarOrcamento`, `NotificarCliente`) não encostam
em `StatusAlteradoEm`.

Cada handler lê `ordemServico.StatusAlteradoEm` **antes** de chamar o método de domínio que faz a
transição (depois da chamada o valor já foi sobrescrito, se o método mudar o status) e calcula
`DateTime.UtcNow - inicioEtapa` como a duração da etapa.

Essa medição é exata sempre que nenhuma outra transição de status acontece entre o início e o fim da
etapa. Isso agora vale para `execucao` (que já era exata) e também para `finalizacao`:

- **`finalizacao`** (`Finalizar` → `Concluir`): antes desta correção, a métrica usava
  `AtualizadoEm` como marco inicial, e como `NotificarCliente` sempre roda entre `Finalizar` e
  `Concluir` e também sobrescrevia `AtualizadoEm`, a métrica media na prática "notificação →
  entrega", não "finalização → entrega" — um viés sistemático no caminho normal, não uma borda rara.
  Com `StatusAlteradoEm` — que `NotificarCliente` não toca, por não mudar `Status` — a métrica passa
  a medir corretamente o intervalo real entre `Finalizar` e `Concluir`.

Uma limitação conhecida permanece, com causa diferente da versão anterior deste documento:

- **`diagnostico`** no caminho alternativo: no caminho feliz (`IniciarDiagnostico` →
  `RegistrarDiagnostico`), a medida reflete corretamente o tempo em diagnóstico. Mas
  `RegistrarDiagnostico` também aceita OS no status `AguardandoAprovacao` (recadastro de diagnóstico
  após um orçamento ser rejeitado — ver `RejeitarOrcamento`), e nesse caminho `Status` não muda:
  `RejeitarOrcamento` mantém a OS em `AguardandoAprovacao`, e o novo `RegistrarDiagnostico` também
  não transiciona o status. Como não existe no domínio nenhum evento que marque o início de um
  *re-diagnóstico* (a OS nunca sai de `AguardandoAprovacao` nesse fluxo), `StatusAlteradoEm` ainda
  aponta para a última transição de status real — a que levou a OS a `AguardandoAprovacao` pela
  primeira vez, possivelmente bem antes da rejeição. A métrica, portanto, mede um intervalo maior
  que o tempo real do recadastro nesse caminho. Corrigir isso exigiria um conceito novo de domínio
  ("reabrir diagnóstico", com uma transição ou timestamp próprio), o que fica fora do escopo desta
  tarefa.

Essa limitação deve ser lida como "o dashboard de tempo médio de recadastro de diagnóstico após
rejeição de orçamento tem viés conhecido nesse caminho específico", não como um bug generalizado —
`diagnostico` no caminho feliz e `execucao` são exatos, e `finalizacao` deixou de ser aproximada.

### `oficina.integracoes.falhas`

- **Tipo:** `Counter<long>` · **Unidade:** `{falha}`
- **Descrição:** "Falhas no processamento de eventos de integração"
- **Tags:** `evento` (nome do tipo do `IIntegrationEvent`, ex.: `OrdemServicoFinalizada`) e
  `handler` (nome do tipo do `IIntegrationEventHandler` que falhou)
- **Alimenta:** "erros e falhas nas integrações" no dashboard, e serve de base para o alerta de
  "falha no processamento de ordens de serviço" (alertar quando a taxa desse contador subir).
- **Emitido em** (na OS; PecasInsumos e Pagamento seguem o mesmo padrão nos seus consumers):
  - **Consumers MassTransit** (`PagamentoAprovadoConsumer`, `PagamentoRecusadoConsumer`,
    `EstoqueBaixaFalhouConsumer`): se o handler da Application lançar exceção (falha técnica), o
    contador é incrementado com o nome do evento e do handler e **a exceção é relançada**, para o retry
    do MassTransit atuar (3 reentregas: 1 s, 5 s, 10 s). Como cada tentativa que falha passa pelo
    consumer, **uma mensagem que esgota o retry pode contar até 4 vezes**.
  - **Handlers de integration event**, em falhas lógicas que não lançam exceção (OS não encontrada;
    orçamento do `PagamentoAprovado` inexistente ou não `Aprovado`; comando de compensação que
    devolveu falha): log de erro + contador, sem relançar — a mensagem é considerada tratada.
  - **`EnviarOrcamentoAoCliente`** (domain event handler do `OrcamentoGerado`): falha ao montar ou
    enviar o e-mail do orçamento — contador com `evento=OrcamentoGerado`, `handler=EnviarOrcamentoAoCliente`;
    a abertura da OS e a publicação de `OrcamentoGeradoIntegrationEvent` seguem normalmente.

## Convenções seguidas

- Nomes de instrumento em minúsculas, separados por ponto; unidades em UCUM (`{ordem}`, `s`,
  `{falha}`).
- Nenhuma tag carrega dado pessoal ou de alta cardinalidade: `tipo` e `etapa` são enums fechados
  de poucos valores; `evento` e `handler` são nomes de tipo (finito, conhecido em tempo de
  compilação) — nunca CPF, nome, e-mail ou id de cliente/ordem de serviço.

## Testes

Cada instrumento tem cobertura em testes unitários usando `MetricCollector<T>`
(`Microsoft.Extensions.Diagnostics.Metrics.Testing`), que assina o `Meter` público exposto por
`OrdensServicoMetrics.Meter` / `IntegracoesMetrics.Meter` e verifica valor + tags:

- `tests/Modules/OrdensServico/OrdemServico.Application.Tests/Commands/GerarOrdemServicoHandlerTests.cs`
- `tests/Modules/OrdensServico/OrdemServico.Application.Tests/Commands/AbrirOrdemServicoCompletaHandlerTests.cs`
- `tests/Modules/OrdensServico/OrdemServico.Application.Tests/Commands/RegistrarDiagnosticoHandlerTests.cs`
- `tests/Modules/OrdensServico/OrdemServico.Application.Tests/Commands/FinalizarOrdemServicoHandlerTests.cs`
- `tests/Modules/OrdensServico/OrdemServico.Application.Tests/Commands/ConcluirOrdemServicoHandlerTests.cs`
- `tests/Modules/OrdensServico/OrdemServico.Infrastructure.Tests/Messaging/SagaConsumersTests.cs` (falha nos consumers da Saga)
- `tests/SharedKernel/SharedKernel.Application.Tests/InMemoryIntegrationEventBusTests.cs`

Os testes cobrem tanto o caminho de sucesso (valor e tags corretos) quanto o de falha (nenhuma
métrica emitida quando o handler retorna erro / lança exceção), além de confirmar que a exceção
continua sendo propagada pelo `InMemoryIntegrationEventBus`.

O comportamento de `StatusAlteradoEm` em si — o que faz a medição de `finalizacao` estar correta —
é coberto no nível de domínio, não de métrica:

- `tests/Modules/OrdensServico/OrdemServico.Domain.Tests/OrdemServico/OrdemServicoStatusAlteradoEmTests.cs`
  cobre que `StatusAlteradoEm` avança em cada método que muda `Status` e, mais importante, que ele
  **não se move** em `RegistrarDiagnostico`, `AprovarOrcamento`, `RejeitarOrcamento` e
  `NotificarCliente` — mesmo quando esses métodos sobrescrevem `AtualizadoEm`. São esses testes de
  "não se move" que impedem alguém de, no futuro, voltar a sujar o campo e quebrar a métrica em
  silêncio.
- `ConcluirOrdemServicoHandlerTests` tem um teste dedicado ao cenário que motivou a correção:
  `NotificarCliente` acontecendo entre `Finalizar` e `Concluir`, verificando que a duração registrada
  para `finalizacao` reflete o intervalo desde `Finalizar`, não desde a notificação.
