# Decisões de Arquitetura Relevantes

> Decisões de design que explicam _por que_ o código é como é. Complementa
> [`estrutura-do-projeto.md`](estrutura-do-projeto.md) (o "o quê" da estrutura) e
> [`clean-architecture.md`](clean-architecture.md) (aderência à Clean Architecture).

---

## Domain Events — só quando há consumidor real

Um domain event existe apenas se houver um handler concreto que consuma e faça algo
significativo. Eventos sem consumidor são _dead code_. Dos ~15 candidatos identificados no
event storming, **3 sobreviveram**:

| Evento | Onde | Consumidores |
|---|---|---|
| `OrcamentoGerado` | OrdemServico.Domain | (1) `PublicarOrcamentoGerado` enfileira `OrcamentoGeradoIntegrationEvent` → decrementa estoque; (2) `EnviarOrcamentoAoCliente` chama `EnviarOrcamento()` no agregado |
| `OrcamentoRejeitado` | OrdemServico.Domain | `PublicarOrcamentoRejeitado` enfileira `OrcamentoRejeitadoIntegrationEvent` → estorna estoque |
| `OrdemServicoFinalizada` | OrdemServico.Domain | `NotificarClienteAoFinalizar` chama `NotificarCliente()` no agregado (stub de log) |

> **Nota da Fase 2:** o evento antes chamado `DiagnosticoConcluido` foi renomeado para
> `OrcamentoGerado` (descrição do diagnóstico virou `string?`) para valer também no novo fluxo
> de _abertura completa_, em que o orçamento nasce sem passar por um diagnóstico registrado.
> Racional completo em [`../planos/refatoracao-domain-events.md`](../planos/refatoracao-domain-events.md).

## TransactionBehavior — orquestração pós-commit

O `TransactionBehavior` do MediatR centraliza toda a orquestração em uma sequência determinística:

1. Handler executa → agregado acumula domain events
2. `CollectDomainEvents()` coleta os eventos antes do commit
3. `SaveChangesAsync()` — **commit**
4. `ClearDomainEvents()` nos agregados
5. `IPublisher.Publish(domainEvent)` para cada evento → handlers de domain event rodam
6. Handlers podem enfileirar integration events em `IPendingIntegrationEvents` ou mutar outros agregados (com `SaveChanges` próprio)
7. `IIntegrationEventBus.Publish()` para cada integration event pendente

Nenhum evento sai antes do commit. Command handlers não sabem nada sobre integration events —
isso é responsabilidade dos domain event handlers.

## Comunicação entre módulos

**Síncrona (ACL):** quando o módulo precisa de resposta imediata (ex.: verificar se cliente
existe ao criar OS). O consumidor define uma _port_ em seu Domain no seu vocabulário; a
Infrastructure implementa um _adapter_ que chama os Contracts do produtor e traduz o resultado.

**Assíncrona (Integration Events):** quando é um fato consumado que outros BCs reagem (ex.:
orçamento gerado → decrementa estoque). Integration events vivem nos `<Modulo>.Contracts`.
Nenhum módulo referencia diretamente Domain ou Application de outro módulo.

Racional completo, com exemplos de código e alternativas descartadas (broker externo, HTTP entre
módulos), em [ADR-007](adrs/007-padrao-de-comunicacao-entre-modulos.md).

## Veiculo é imutável

Placa, modelo, marca e ano são definidos na criação e não têm métodos de mutação. Não existe
endpoint `PUT /veiculos/{id}` — decisão intencional de domínio.

## PATCH parcial para atualizações

`AtualizarClienteCommand` e `AtualizarServicoCommand` aceitam campos anuláveis. Campos `null`
são ignorados pelo handler. Validação com `.When(campo is not null)` no FluentValidation. Campos
imutáveis (documento, email, nome do serviço) nunca são expostos para atualização.

## Decremento de estoque na geração do orçamento

O estoque é reservado quando o orçamento é gerado (ao concluir o diagnóstico, ou já na abertura
completa), não na aprovação. Isso evita a condição de corrida onde dois orçamentos disputam o
mesmo estoque. A rejeição estorna via `OrcamentoRejeitadoIntegrationEvent`.

## Dois emissores de token e autorização por papel

A aplicação **valida** tokens de dois emissores, mas só **emite** um deles:

| Papel | Emissor | Credencial | `iss` |
|---|---|---|---|
| `Oficina` | esta aplicação (`POST /api/v1/auth/login`) | email + senha | `oficina-mecanica-app` |
| `Cliente` | Function Serverless (repositório separado, ainda não existe) | CPF | `oficina-mecanica-auth` |

Ambos são HS256 com o mesmo segredo compartilhado e carregam a claim `role`. As rotas deixaram
de exigir apenas "estar autenticado": cada ação declara o papel exigido. Além do papel, um token
`Cliente` só opera sobre a **própria** OS — o `sub` do token é comparado com o dono, e o acesso
à OS de outro cliente devolve **403**, não 404.

Consequência prática: `GET /api/v1/ordens-servico?clienteId=...` **deixou de ser anônima**
(passou a exigir `Oficina`) e o acompanhamento pelo cliente virou
`GET /api/v1/ordens-servico/acompanhamento`, filtrado pelo token. Racional completo em
[`rfcs/001-estrategia-de-autenticacao.md`](rfcs/001-estrategia-de-autenticacao.md) e
[`adrs/003-dois-emissores-e-autorizacao-por-papel.md`](adrs/003-dois-emissores-e-autorizacao-por-papel.md);
mapa de rotas por papel no [README](../../README.md#mapa-de-rotas-por-papel).

## Result\<T\> — erros de negócio sem exceptions

Handlers retornam `Result<T>`. O `TransactionBehavior` não persiste se `result.IsFailure`.
Controllers mapeiam falhas para Problem Details (RFC 7807). Exceptions não tratadas passam pelo
`CustomExceptionHandler` global.

---

## Ciclo de Vida de uma OS

A OS tem **dois pontos de entrada** (adição da Fase 2, retrocompatível):

**Fluxo 1 — com diagnóstico (Fase 1):** o atendimento começa sem saber o que será feito.

```
POST /api/v1/ordens-servico             → status: Recebida
PATCH /{id}/iniciar-diagnostico         → status: EmDiagnostico
PATCH /{id}/registrar-diagnostico       → cria orçamento, envia ao cliente, decrementa estoque
                                          (automático via domain events)
                                          → status: AguardandoAprovacao, orçamento: Enviado
```

**Fluxo 2 — abertura completa (Fase 2):** o cliente já chega pedindo serviços/peças; pula
Recebida/Diagnóstico e o orçamento nasce direto.

```
POST /api/v1/ordens-servico/completa    → cria orçamento, envia ao cliente, decrementa estoque
                                          → status: AguardandoAprovacao, orçamento: Enviado
```

**Daí em diante, ambos os fluxos convergem:**

```
PATCH /{id}/aprovar-orcamento           → orçamento: Aprovado
  (ou) /rejeitar-orcamento             → orçamento: Rejeitado, estoque estornado
PATCH /{id}/executar                    → status: EmExecucao
PATCH /{id}/finalizar                   → status: Finalizada, notificado_em preenchido (automático)
PATCH /{id}/concluir                    → status: Entregue
```

Consulta pública para o cliente acompanhar: `GET /api/v1/ordens-servico/acompanhamento`
(lista as OS em andamento) e `GET /api/v1/ordens-servico/{id}/status`.

### Listagem de acompanhamento — ordenação e exclusão lógica (Fase 2)

A listagem atende à regra exigida na Fase 2 (`ListarOrdensParaAcompanhamentoReadModelImpl`):

- **Ordenação por prioridade de status:** `EmExecucao` → `AguardandoAprovacao` → `EmDiagnostico`
  → `Recebida`; dentro do mesmo status, **as mais antigas primeiro** (`ThenBy(CriadoEm)`).
- **Exclusão lógica (não física):** OS `Finalizada` e `Entregue` são **filtradas da listagem**
  (via `WHERE` nos status ativos), sem apagar nada do banco.

> Event storming completo: [`event-storming.md`](event-storming.md).
