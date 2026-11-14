# Estrutura do Projeto — Oficina Mecânica (MVP DDD)

> **Fase 4:** este documento registra a estrutura do **monólito modular** das fases 1–3. Na Fase 4 cada
> módulo virou um microsserviço, em repositório próprio, com a mesma estrutura interna de camadas
> descrita aqui, o `InMemoryIntegrationEventBus` deu lugar à mensageria (MassTransit sobre SNS/SQS) e as
> consultas entre módulos viraram chamadas HTTP — ver
> [ADR-009](adrs/009-decomposicao-em-microsservicos.md), [ADR-010](adrs/010-saga-coreografada-com-masstransit.md) e
> [`diagramas/componentes.md`](diagramas/componentes.md). O que está abaixo sobre Domain, Application,
> Adapters e testes continua valendo dentro de cada serviço.

> Documento de decisões de arquitetura e estrutura de projeto, consolidado durante sessão de brainstorming.
> Serve como referência viva para o desenvolvimento incremental do MVP.

---

## 1. Contexto

Este projeto é o Tech Challenge da Fase 1 da pós-graduação (disciplina de DDD). Trata-se da primeira versão (MVP) do back-end de um **Sistema Integrado de Atendimento e Execução de Serviços** para uma oficina mecânica de médio porte.

**Tecnologia base:** .NET (C#) + PostgreSQL.

**Requisitos relevantes do PDF do desafio:**

- Back-end monolítico (o PDF permite "monolito em camadas", mas escolhemos uma modularização mais estrita — justificativa adiante).
- Aplicação de Domain-Driven Design.
- APIs RESTful documentadas via Swagger.
- Autenticação JWT para APIs administrativas.
- Validação de dados sensíveis (CPF/CNPJ, placa).
- Testes unitários e de integração, com **cobertura mínima de 80% nos domínios críticos**.
- Dockerfile + docker-compose para orquestrar o ambiente completo.
- README explicativo para execução local.

**Bounded Contexts identificados no event storming:**

1. **Cadastro** — Cliente, Veículo, Serviço (catálogo).
2. **Ordem de Serviço** — ciclo de vida da OS, orçamento, status, execução.
3. **Peças e Insumos** — estoque, busca e validação de disponibilidade.

**Objetivo pessoal adicional:** este é um projeto de estudo pessoal, apartado da versão do time. A estrutura aqui descrita prioriza **aprendizado de DDD aplicado em .NET** e **preparação para extração futura em microsserviços** nas próximas fases da pós-graduação.

---

## 2. Princípios que guiaram as decisões

1. **Fronteira de Bounded Context é de primeira classe.** O BC não é só um namespace — é uma unidade física do código, com assembly próprio, schema próprio, e contratos próprios.
2. **Preparação para extração de microsserviços.** Cada decisão tem que passar no teste: "quanto trabalho dá pra virar serviço separado?". O ideal é: mover alguns `.csproj`, trocar adapters por HTTP clients, trocar bus in-process por mensageria. Nada no Domain/Application do módulo deveria precisar mudar.
3. **YAGNI ruthlessly.** Não criamos VOs de negócio nem abstrações agora se elas não têm uso imediato. Crescem sob demanda.
4. **Compilador como aliado da fronteira.** Onde possível, usamos referências de projeto para IMPEDIR (em compile-time) acoplamento indevido entre BCs ou entre camadas.
5. **Rastreabilidade Event Storming ↔ código.** Cada CMD do event storming deve ter um arquivo correspondente no código (um Command + Handler).

---

## 3. Arquitetura macro — Modular Monolith

A escolha é **Modular Monolith**, não camadas "horizontais" clássicas (Domain/Application/Infrastructure/Api como projetos únicos).

### Por que não o clássico "4 projetos em camadas"

- O PDF permite arquitetura em camadas, mas ela apaga o trabalho de DDD feito no event storming: os 3 BCs ficariam misturados dentro do mesmo `Domain.dll`, `Application.dll`, etc.
- Sem fronteira física, nada impede (em compile-time) que código de Ordem de Serviço instancie diretamente entidades de Cadastro ou de Peças e Insumos.
- Para extrair um microsserviço depois, seria necessário **primeiro** fazer o split por BC — trabalho que o Modular Monolith já deixa pronto.

### Por que não "um projeto por BC com camadas internas"

Foi a alternativa mais séria considerada. Seus pontos fortes: mais compacto (~5 projetos), boundary real entre BCs. Pontos fracos que pesaram:

- Camadas internas ficam como pastas, sem enforcement de compilação: `Domain/` poderia acabar referenciando `Infrastructure/` (quebra do Dependency Inversion Principle).
- Na hora de extrair, o projeto do BC teria que ser **splitado em 3-4 projetos de camada** antes de virar serviço. É refactor grande, feito sob pressão.

### Por que Modular Monolith "por BC × camada"

- Cada BC tem 5 projetos próprios, cada camada é um assembly separado, o compilador enforça DIP e os limites do BC.
- Para extrair: `dotnet new` no novo serviço, move os 5 projetos pra lá, troca adapters, pronto.
- Custo: 15-20 projetos no total. Em troca, cada um é minúsculo e focado. Build incremental do .NET lida bem. IDE lida bem.

---

## 4. Esqueleto de pastas e projetos

```
oficina-mecanica-v2/
├── src/
│   ├── SharedKernel/
│   │   ├── SharedKernel.Domain/
│   │   └── SharedKernel.Application/
│   ├── Modules/
│   │   ├── Cadastro/
│   │   │   ├── Cadastro.Domain/
│   │   │   ├── Cadastro.Application/
│   │   │   ├── Cadastro.Infrastructure/
│   │   │   ├── Cadastro.Presentation/
│   │   │   └── Cadastro.Contracts/
│   │   ├── OrdensServico/
│   │   │   ├── OrdemServico.Domain/
│   │   │   ├── OrdemServico.Application/
│   │   │   ├── OrdemServico.Infrastructure/
│   │   │   ├── OrdemServico.Presentation/
│   │   │   └── OrdemServico.Contracts/
│   │   └── PecasInsumos/
│   │       ├── PecasInsumos.Domain/
│   │       ├── PecasInsumos.Application/
│   │       ├── PecasInsumos.Infrastructure/
│   │       ├── PecasInsumos.Presentation/
│   │       └── PecasInsumos.Contracts/
│   └── Bootstrap/
│       └── Api/
├── tests/
│   ├── Modules/
│   │   ├── Cadastro/
│   │   │   ├── Cadastro.Domain.Tests/
│   │   │   └── Cadastro.Application.Tests/
│   │   ├── OrdemServico/
│   │   │   ├── OrdemServico.Domain.Tests/
│   │   │   └── OrdemServico.Application.Tests/
│   │   └── PecasInsumos/
│   │       ├── PecasInsumos.Domain.Tests/
│   │       └── PecasInsumos.Application.Tests/
│   └── IntegrationTests/
├── docker-compose.yml
├── OficinaMecanica.slnx
└── README.md
```

**Contagem:** 2 (SharedKernel) + 15 (módulos: 3 × 5) + 1 (Bootstrap/Api) + 6 (testes unitários: 3 × 2) + 1 (IntegrationTests) = **25 projetos**.

---

## 5. Papel de cada projeto

### 5.1 SharedKernel

Código genuinamente compartilhado por **todos** os módulos. Regra de ouro: **na dúvida, não entra**. Duplicação entre módulos é preferível a acoplamento indevido via SharedKernel.

#### `SharedKernel.Domain`

Tipos-base usados por qualquer Domain:

- `Entity<TId>` — base de entidade, igualdade por identidade.
- `AggregateRoot<TId>` — estende `Entity`, acumula `IReadOnlyCollection<IDomainEvent> DomainEvents`, com `AddDomainEvent`/`ClearDomainEvents`.
- `ValueObject` — base com `Equals` estrutural por componentes.
- `IDomainEvent` — marker interface para eventos de domínio.
- `Result<T>` + `Error` — tipo de retorno para modelar erros de negócio sem exceptions. (Alternativa ao `throw`; usar com parcimônia.)
- `IIntegrationEvent` — marker interface para eventos de integração cross-module. (Fica aqui porque os `<Modulo>.Contracts` precisam referenciá-lo.)

#### `SharedKernel.Application`

Abstrações usadas por qualquer Application:

- `IIntegrationEventBus` — interface do bus (`Publish<T>(T evento)`).
- `IIntegrationEventHandler<T>` — interface dos handlers que consomem eventos de integração.
- `InMemoryIntegrationEventBus` — implementação in-process padrão do bus, via `IServiceProvider.GetServices<IIntegrationEventHandler<T>>()`. (Alternativa: implementação pode ficar numa `SharedKernel.Infrastructure` separada; para o MVP, manter aqui simplifica.)
- Pipeline behaviors do MediatR: `ValidationBehavior`, `LoggingBehavior`, `TransactionBehavior`. São pieces de cross-cutting registrados uma vez e aplicados a todo command/query.
- `IPendingIntegrationEvents` — abstração scoped (wrapper sobre fila de delegates) que resolve o problema de ordenação entre commit e publicação de eventos de integração (ver decisão abaixo).
- `IUnitOfWork` — interface que os `DbContext` de cada módulo implementam, expondo `SaveChangesAsync`, `CollectDomainEvents` e `ClearDomainEvents`. Permite ao `TransactionBehavior` orquestrar domain events sem depender de EF Core diretamente.

> **Decisão — `TransactionBehavior` com fluxo pós-commit em duas etapas:**
>
> O `TransactionBehavior` orquestra a sequência após o handler retornar com sucesso:
> 1. Coleta domain events acumulados nos agregados via `IUnitOfWork.CollectDomainEvents()`.
> 2. Persiste com `SaveChangesAsync()` e limpa os events do agregado.
> 3. Despacha cada domain event via `IPublisher` (MediatR) — handlers de domain event são chamados aqui.
> 4. Domain event handlers podem enfileirar integration events em `IPendingIntegrationEvents`.
> 5. Itera `IPendingIntegrationEvents` e publica cada evento via `IIntegrationEventBus`.
>
> Esta separação garante: (a) nenhum evento sai antes do commit; (b) command handlers não precisam saber sobre integration events — isso é responsabilidade dos handlers de domain event. `IDomainEvent` implementa `INotification` do MediatR para ser despachado na etapa 3.

#### O que NÃO entra no SharedKernel

- Agregados (`Cliente`, `Veiculo`, `OrdemServico`, etc.) — moram no Domain do BC dono.
- DTOs ou interfaces específicas de um módulo — moram em `<Modulo>.Contracts`.
- `IRepository<T>` genérico — cada módulo define seu próprio `IClienteRepository`, `IOrdemServicoRepository`, com as queries que **realmente** precisa. Repositório genérico é anti-padrão em DDD: obscurece a intenção, induz vazamento de `IQueryable`.
- `IUnitOfWork` — cada `DbContext` já é um UoW; cada módulo tem o seu. Abstrair "um UoW pra todos" seria convidar transação cruzada entre BCs, exatamente o que queremos evitar.
- Utilitários técnicos (logging, auth, middlewares) — vão no `Bootstrap/Api` ou num `BuildingBlocks.Infrastructure` futuro se fizer sentido.

#### Value Objects compartilhados (adicionar sob demanda)

**Não criar preventivamente.** Quando surgir a necessidade real durante a implementação, candidatos naturais são:

- `Cpf`, `Cnpj` (com validação).
- `Placa` (Mercosul + antiga).
- `Dinheiro` (`decimal Valor`, `string Moeda`).

Se só um módulo usa, mantenha dentro do Domain desse módulo. Só promove para SharedKernel quando dois ou mais módulos precisam do MESMO conceito com o MESMO shape.

---

### 5.2 `<Modulo>.Domain`

O coração do BC. Contém:

- **Agregados** — `AggregateRoot`s e suas entidades filhas.
- **Value Objects específicos do BC.**
- **Domain Events** — eventos internos (ex.: `OrcamentoFoiGerado`).
- **Interfaces de repositório** — específicas por agregado, não genéricas.
- **Ports de ACL** — interfaces no vocabulário deste BC, implementadas na Infrastructure via adapters que consomem `<OutroModulo>.Contracts` (ver seção 7).

**Organização interna: por agregado, não por tipo.**

Justificativa: em DDD, a unidade de raciocínio é o agregado. Abrir uma pasta e ver tudo que pertence àquele agregado (entidade raiz, entidades filhas, VOs específicos, eventos internos, interface do repo) é mais coerente do que caçar peças em `Entities/`, `ValueObjects/`, `Events/`, `Repositories/`. Reforça visualmente a regra "um agregado, uma transação, uma fronteira de invariantes".

Exemplo (a criar conforme for implementando, **não precisa criar todos agora**):

```
OrdemServico.Domain/
├── Ordens/                           ← agregado OrdemServico
│   ├── OrdemServico.cs
│   ├── OrdemServicoId.cs
│   ├── ItemServico.cs
│   ├── ItemPeca.cs
│   ├── StatusOrdemServico.cs
│   ├── Events/
│   │   ├── OrdemServicoGerada.cs
│   │   └── OrdemFinalizada.cs
│   └── IOrdemServicoRepository.cs
├── Orcamentos/                       ← agregado Orcamento
│   ├── Orcamento.cs
│   └── ...
└── Ports/                            ← ACL ports consumidos pelo Domain
    ├── IClienteInfoPort.cs
    └── IVeiculoInfoPort.cs
```

**Referências permitidas:** apenas `SharedKernel.Domain`. O Domain não referencia Application, Infrastructure, Contracts de ninguém — nem seus, nem de outro módulo.

> **Decisão — imutabilidade do agregado `Veiculo`:** `placa`, `modelo`, `marca` e `ano` são imutáveis após a criação. O agregado não expõe métodos de mutação por decisão de modelagem: no contexto deste sistema, os dados de registro de um veículo não se alteram. Não existe caso de uso `AtualizarVeiculo`. Agregar invariantes que não mudam elimina toda uma classe de bugs e simplifica o modelo.

---

### 5.3 `<Modulo>.Application`

Orquestração dos casos de uso, usando CQRS com MediatR (v12, versão livre sob MIT).

**Organização interna: vertical slice por caso de uso.**

Cada command/query fica numa pasta **própria** contendo `Command`, `Handler`, `Validator` e `Response` juntos. Ao trabalhar num caso de uso, você abre uma pasta e tem tudo dele na frente — e nada mais. Alinha perfeitamente com o event storming: cada CMD amarelo-azul do diagrama vira literalmente uma pasta.

Exemplo:

```
OrdemServico.Application/
├── Ordens/
│   ├── Commands/
│   │   ├── GerarOrdemServico/
│   │   │   ├── GerarOrdemServicoCommand.cs
│   │   │   ├── GerarOrdemServicoHandler.cs
│   │   │   ├── GerarOrdemServicoValidator.cs
│   │   │   └── GerarOrdemServicoResponse.cs
│   │   ├── IniciarDiagnostico/
│   │   ├── GerarOrcamento/
│   │   ├── AprovarOrcamento/
│   │   ├── ExecutarOrdemServico/
│   │   ├── FinalizarOrdemServico/
│   │   └── ConcluirOrdemServico/
│   └── Queries/
│       ├── ObterOrdemServicoPorId/
│       └── ListarOrdensPorCliente/
└── IntegrationEventHandlers/         ← reage a eventos vindos de outros módulos
```

**Sobre MediatR:** é uma lib de mediator in-process. Controllers publicam um `IRequest`; o MediatR descobre e invoca o handler certo; pipeline behaviors adicionam validação, logging e transação sem poluir o handler. Da v13 em diante virou comercial; **usamos v12**, que é MIT e suficiente para o MVP.

**Referências permitidas:** `<Modulo>.Domain`, `SharedKernel.Domain`, `SharedKernel.Application`, `<Modulo>.Contracts` e **`<OutroModulo>.Contracts`** (apenas quando precisa consumir).

> **Decisão — Application referencia seus próprios Contracts:** `<Modulo>.Application` depende de `<Modulo>.Contracts` para instanciar e enfileirar eventos de integração (ex.: `ClienteCadastradoIntegrationEvent`) via `IPendingIntegrationEvents`. A alternativa — delegar a criação do evento a um handler de Infrastructure — adiciona complexidade não justificada para o MVP.
>
> Sobre reutilização de DTOs: DTOs de Contracts (ex.: `ClienteDto`) **podem** ser reutilizados como tipo de retorno de queries quando o shape corresponde exatamente ao que o caso de uso precisa. Quando o caso de uso precisa de mais campos, define-se um tipo de resposta específico na Application. Tipos de retorno de commands (ex.: `CadastrarClienteResponse`) são sempre definidos na Application, pois representam o resultado da operação (como o novo ID gerado), não um shape de leitura.

> **Decisão — interfaces de leitura para listagens ficam na Application:** interfaces de repositório de domínio (`IClienteRepository`, etc.) servem ao modelo de escrita — buscam um agregado por id e salvam. Não são o lugar certo para métodos de filtro e listagem. Para casos de uso de consulta que precisam de projeções planas (listas, filtros), define-se uma interface de leitura **dentro da própria Application** (ex.: na pasta da query ou numa subpasta `ReadModel`). A Infrastructure implementa essa interface com uma projeção direta do `DbContext` para DTOs simples, sem hidratar o agregado completo. Esse padrão mantém as interfaces de repositório de domínio enxutas e focadas na escrita.

> **Decisão — `AtualizarCliente` usa PATCH parcial, não PUT:** `Documento` e `Email` são imutáveis após o cadastro (o documento é identidade do cliente; o email não possui caso de uso de alteração no domínio). Como o recurso nunca pode ser substituído por inteiro, `PUT /clientes/{id}` não faz sentido semântico. A atualização é exposta como dois endpoints `PATCH` independentes: `PATCH /clientes/{id}/nome` e `PATCH /clientes/{id}/telefone`. O command `AtualizarClienteCommand` declara `Nome` e `Telefone` como `string?`; campos `null` são ignorados pelo handler. O validator aplica regras de formato apenas quando o campo é não-nulo (`.When(x => x.Campo is not null)`), sem exigir que ambos sejam enviados juntos.

---

### 5.4 `<Modulo>.Infrastructure`

Implementações técnicas:

- **Persistence** — `<Modulo>DbContext`, `IEntityTypeConfiguration`s, Migrations EF Core, implementações dos repositórios.
- **ACL Adapters** — classes que implementam os `Ports` do Domain consumindo os `Contracts` de outro módulo e **traduzindo** o resultado para o vocabulário deste BC.
- **Integration Event Handlers** (impls técnicas quando precisarem de IO) — se o handler for puro orquestração, fica na Application; se precisar de repositório, pode ficar aqui.
- **Module Registration** — classe estática `<Modulo>Module` com método de extensão `Add<Modulo>Module(IServiceCollection, IConfiguration)` que registra:
  - `DbContext`
  - MediatR scan desse módulo
  - Repositórios
  - Adapters de ACL
  - Handlers de integration events que o módulo assina

Exemplo:

```
OrdemServico.Infrastructure/
├── Persistence/
│   ├── OrdemServicoDbContext.cs
│   ├── Configurations/
│   ├── Migrations/
│   └── Repositories/
├── Acl/
│   ├── ClienteInfoAdapter.cs
│   ├── VeiculoInfoAdapter.cs
│   └── PecaDisponibilidadeAdapter.cs
├── Contracts/                        ← implementações das queries publicadas por este módulo
└── OrdemServicoModule.cs
```

**Referências permitidas:** `<Modulo>.Application` (e portanto Domain via transitivo), `<Modulo>.Contracts`, `<OutroModulo>.Contracts`, `SharedKernel.*`, pacotes de infraestrutura (EF Core, Npgsql, etc.).

---

### 5.5 `<Modulo>.Presentation`

Controllers REST e request/response HTTP **específicos deste BC**.

Justificativa para ter controllers por módulo (em vez de num único projeto `Api` central): os endpoints do BC são a "linguagem HTTP" do agregado — `POST /clientes` e `POST /veiculos` só fazem sentido em Cadastro; `PATCH /ordens-servico/{id}/iniciar-diagnostico` só faz sentido em Ordem de Serviço. Manter esse vínculo físico reforça que o módulo é auto-contido e, quando extrair, os controllers vão junto.

```
OrdemServico.Presentation/
├── Controllers/
│   ├── OrdensServicoController.cs
│   └── OrcamentosController.cs
└── Models/                           ← só quando fizer sentido ter shape HTTP distinto do Command
```

Por padrão, **controllers recebem o `Command` direto** no `[FromBody]` e despacham via MediatR — dispensa `Models/`. Criar request models HTTP separados só quando houver necessidade real (ex.: formato HTTP que não bate com o shape do command).

**Referências permitidas:** `<Modulo>.Application`, `SharedKernel.*`.

**Integração com o host:** o `Bootstrap/Api` registra os controllers de cada módulo via `AddApplicationPart(typeof(SomeControllerFromModule).Assembly)`. Isso permite que o ASP.NET descubra os controllers sem precisar mover código para o host.

---

### 5.6 `<Modulo>.Contracts`

A **única fronteira pública** do módulo — o "contrato de API" que outros módulos podem consumir.

```
OrdemServico.Contracts/
├── Queries/                          ← interfaces síncronas publicadas pelo módulo
│   └── IOrdemServicoResumoQuery.cs
├── Dtos/                             ← shapes retornados por Queries e payloads de Events
│   └── OrdemServicoResumoDto.cs
└── IntegrationEvents/                ← eventos publicados por este módulo
    ├── ItemPecaEventDto.cs
    ├── OrcamentoGeradoIntegrationEvent.cs    ← PecasInsumos decrementa estoque
    └── OrcamentoRejeitadoIntegrationEvent.cs ← PecasInsumos estorna estoque
```

**Referências permitidas:** apenas `SharedKernel.Domain` (para usar `IIntegrationEvent`).

**Por que um projeto separado:** outros módulos precisam conhecer só esse shape público, não o Domain/Application interno. Separar em projeto garante que `<OutroModulo>.Application` só pode puxar este `.Contracts` — nunca o `.Domain` real, nunca internals. Quando virar microsserviço, este `.Contracts` continua existindo e descreve literalmente a API REST/gRPC que o serviço expõe.

---

### 5.7 `Bootstrap/Api` (host)

Ponto de entrada da aplicação. Idealmente magro — só conecta peças:

- `Program.cs` monta o pipeline ASP.NET.
- Registra JWT, Swagger, CORS, health checks, exception middleware global.
- Chama cada `AddXxxModule(configuration)` dos módulos.
- Registra a infra do bus de integração (`AddIntegrationEventBus()`).
- Usa `AddApplicationPart` para expor os controllers dos módulos.
- Contém o Dockerfile da aplicação.

**Referências permitidas:** todos os `<Modulo>.Presentation` e `<Modulo>.Infrastructure` (pra poder chamar `AddXxxModule`), `SharedKernel.*`.

---

## 6. Persistência — PostgreSQL com schema por módulo

### Banco

**PostgreSQL**, justificado pelos seguintes pontos (relevantes pro README do desafio):

- Schemas nativos de primeira classe — casam perfeitamente com modular monolith.
- Npgsql + EF Core são maduros, documentação boa.
- Padrão do ecossistema cloud-native/microsserviços — quando extrair serviços, Postgres é o default.
- Docker trivial (`postgres:16`), sem dor de licenciamento.

### Topologia

- **1 banco físico único** no MVP (`oficina_mecanica`).
- **1 schema por módulo**: `cadastro`, `ordem_servico`, `pecas_insumos`.
- **1 `DbContext` por módulo**, com `HasDefaultSchema("<schema_do_modulo>")` no `OnModelCreating`.
- **1 conjunto de migrations por módulo**, isolado no próprio projeto `Infrastructure`.

### Regra crítica: nada de FK cross-schema

Referências entre BCs são **apenas por id** (GUID), sem foreign key no banco e sem navigation property no EF. Exemplo: `ordem_servico.ordem_servico.cliente_id` é uma coluna `uuid` solta — o banco não valida que o cliente existe. Quem valida é a **Application**, via ACL port.

Por que essa "restrição autoimposta":

- É exatamente o que você terá em microsserviços: nenhum JOIN cross-service.
- Força a comunicação pelas vias corretas (ACL sync ou Integration Events async), e já habitua o código a isso.
- Prepara migração futura sem surpresas.

### Mapeamento dos dados (referência)

**Schema `cadastro`**: `cliente`, `veiculo`, `servico`.

**Schema `ordem_servico`**: `ordem_servico`, `os_servico`, `os_peca`, `orcamento`.
- `os_servico.servico_id` referencia `cadastro.servico` apenas por id (sem FK).
- `os_peca.peca_insumo_id` referencia `pecas_insumos.peca_insumo` apenas por id (sem FK).
- `ordem_servico.cliente_id` / `.veiculo_id` referenciam `cadastro.*` apenas por id.

**Schema `pecas_insumos`**: `peca_insumo` (com coluna de estoque).

---

## 7. Comunicação entre módulos

Duas formas complementares, escolhidas caso a caso:

### 7.1 Síncrona — ACL sobre contratos publicados

Usada quando o consumidor **precisa de resposta agora** (ex.: ao gerar OS, confirmar que o cliente existe).

**Mecânica:**

1. O módulo **produtor** publica um **contrato de consulta** em seu `.Contracts`, no vocabulário do produtor. Exemplo:
   ```csharp
   // Cadastro.Contracts
   public interface ICadastroClienteQuery {
       Task<ClienteResumoDto?> ObterPorId(Guid id);
   }
   public record ClienteResumoDto(Guid Id, string Nome, string Documento, bool Ativo);
   ```
2. O **produtor** implementa esse contrato em sua `Infrastructure` (tipicamente consultando o próprio DbContext).
3. O módulo **consumidor** define uma **porta ACL** dentro do seu próprio Domain, no seu próprio vocabulário:
   ```csharp
   // OrdemServico.Domain/Ports
   public interface IClienteInfoPort {
       Task<ClienteInfo?> Obter(ClienteId id);
   }
   ```
4. O **consumidor** implementa a porta via um **adapter** na sua `Infrastructure`, que chama o contrato do produtor e **traduz** o DTO para o tipo do consumidor:
   ```csharp
   // OrdemServico.Infrastructure.Acl
   internal class ClienteInfoAdapter : IClienteInfoPort {
       private readonly ICadastroClienteQuery _cadastro;
       public async Task<ClienteInfo?> Obter(ClienteId id) {
           var dto = await _cadastro.ObterPorId(id.Value);
           return dto is null ? null : new ClienteInfo(new ClienteId(dto.Id), dto.Nome, dto.Ativo);
       }
   }
   ```

**Por que dois tipos (DTO do produtor + tipo interno do consumidor):** o Domain do consumidor fala a língua dele. Se o Cadastro mudar "nome completo" para "razão social", o consumidor não muda uma linha — só o adapter traduz. Esse é o valor clássico da ACL.

**Extração pra microsserviço:** o adapter deixa de instanciar `ICadastroClienteQuery` local e passa a fazer `HttpClient.GetAsync("/clientes/{id}")`. Nada no Domain/Application do consumidor muda.

### 7.2 Assíncrona — Integration Events

Usada quando é **fato consumado que outros podem querer saber** (ex.: orçamento foi aprovado → decrementar estoque; OS foi finalizada → (futuro) notificar cliente).

**Mecânica:**

1. O **produtor** define o evento em seu `.Contracts`:
   ```csharp
   // OrdemServico.Contracts
   public record OrcamentoGeradoIntegrationEvent(
       Guid EventId, DateTime OcorridoEm,
       Guid OrdemServicoId, Guid OrcamentoId,
       IReadOnlyList<ItemPecaEventDto> Pecas
   ) : IIntegrationEvent;
   ```
2. O agregado do produtor emite um **domain event** (ex.: `DiagnosticoConcluido`). O `TransactionBehavior` persiste, então despacha o domain event via `IPublisher`. Um **handler de domain event** no módulo produtor reage e enfileira o integration event via `IPendingIntegrationEvents`. Após todos os domain event handlers rodarem, o `TransactionBehavior` itera a fila e despacha via `IIntegrationEventBus`. Nenhum evento sai antes do commit.
3. O **consumidor** implementa um handler em sua `Application`:
   ```csharp
   // PecasInsumos.Application.IntegrationEventHandlers
   public class DecrementarEstoqueQuandoOrcamentoGerado
       : IIntegrationEventHandler<OrcamentoGeradoIntegrationEvent> { ... }
   ```
4. O **consumidor** registra o handler em seu próprio `AddPecasInsumosModule` (ver seção 8).

**Implementação do bus no MVP:** in-process, `InMemoryIntegrationEventBus` faz `serviceProvider.GetServices<IIntegrationEventHandler<T>>()` e invoca cada um. Suficiente pro monolito.

**Extração pra microsserviço:** troca a implementação do `IIntegrationEventBus` por um publisher RabbitMQ/Kafka/ServiceBus. Nenhum handler muda.

### 7.3 Domain events vs Integration events

Não confundir:

- **Domain event** — fato relevante **dentro** do agregado/BC (ex.: `DiagnosticoConcluido`). Reside em `<Modulo>.Domain`. Implementa `INotification` do MediatR. Despachado pelo `TransactionBehavior` pós-commit via `IPublisher`. Não atravessa fronteira de BC.
- **Integration event** — fato que **outros BCs** podem querer saber (ex.: `OrcamentoGeradoIntegrationEvent`). Reside em `<Modulo>.Contracts`. Vai pelo `IIntegrationEventBus`.

Padrão adotado: um **handler de domain event** (implementa `INotificationHandler<TDomainEvent>`) reage ao domain event e enfileira o integration event em `IPendingIntegrationEvents`. O command handler não sabe nada sobre integration events.

> **Critério de existência de domain events:** um domain event só deve existir se houver um consumidor real (handler de domain event dentro do módulo). Eventos sem consumidores são dead code. Nem toda transição de estado precisa de um domain event — apenas quando há uma reação real a modelar.

### 7.4 O que NÃO fazer

- **Nunca** referenciar diretamente `<OutroModulo>.Application` ou `<OutroModulo>.Domain`. As únicas referências permitidas entre módulos são para `<OutroModulo>.Contracts`.
- **Nunca** expor entidades ou agregados em `.Contracts`. `.Contracts` só expõe DTOs simples, interfaces e eventos.
- **Nunca** usar o SharedKernel como "mural de recados" entre módulos. Se vira, o SharedKernel morreu.

---

## 8. Registro de DI e ciclo de startup

### Cada módulo registra a si mesmo

```csharp
// PecasInsumos.Infrastructure/PecasInsumosModule.cs
public static class PecasInsumosModule
{
    public static IServiceCollection AddPecasInsumosModule(
        this IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<PecasInsumosDbContext>(opt =>
            opt.UseNpgsql(cfg.GetConnectionString("Default")));

        services.AddMediatR(c =>
            c.RegisterServicesFromAssembly(typeof(PecasInsumosModule).Assembly));

        services.AddScoped<IPecaInsumoRepository, PecaInsumoRepository>();

        // Contrato publicado
        services.AddScoped<IPecasInsumosDisponibilidadeQuery, PecasInsumosDisponibilidadeQuery>();

        // Integration events que o módulo assina
        services.AddScoped<
            IIntegrationEventHandler<OrcamentoGeradoIntegrationEvent>,
            DecrementarEstoqueQuandoOrcamentoGerado>();
        services.AddScoped<
            IIntegrationEventHandler<OrcamentoRejeitadoIntegrationEvent>,
            IncrementarEstoqueQuandoOrcamentoRejeitado>();

        return services;
    }
}
```

### Bootstrap apenas compõe

```csharp
// Bootstrap/Api/Program.cs
builder.Services
    .AddIntegrationEventBus()
    .AddCadastroModule(builder.Configuration)
    .AddOrdemServicoModule(builder.Configuration)
    .AddPecasInsumosModule(builder.Configuration);

builder.Services
    .AddControllers()
    .AddApplicationPart(typeof(Cadastro.Presentation.AnyController).Assembly)
    .AddApplicationPart(typeof(OrdemServico.Presentation.AnyController).Assembly)
    .AddApplicationPart(typeof(PecasInsumos.Presentation.AnyController).Assembly);
```

**Princípio:** o Bootstrap ignora o que cada módulo faz internamente; só sabe como ativá-los. Isso preserva a autonomia de cada módulo — novo handler, novo assinante, novo repositório: tudo muda dentro do módulo. O Bootstrap permanece estável.

---

## 9. Segurança e aspectos transversais (requisitos do PDF)

- **JWT** — registrado no `Bootstrap/Api` (`AddAuthentication().AddJwtBearer(...)`). Controllers anotados com `[Authorize]`, exceto consultas públicas pro cliente acompanhar a OS.
- **Swagger** — registrado no Bootstrap com `AddSwaggerGen`. Inclui XML docs dos `.Contracts` e Controllers de cada módulo.
- **Validação de dados sensíveis (CPF/CNPJ, placa)** — implementada como VOs no Domain do módulo que os usa (Cadastro principalmente). Usar FluentValidation nos Commands para falhar cedo, no pipeline behavior, antes do handler.
- **Tratamento de erros** — `Result<T>` para erros de negócio; exception middleware global no Bootstrap para erros não previstos, com resposta no formato Problem Details (RFC 7807).

---

## 10. Testes

### 10.1 Estrutura

```
tests/
├── Modules/
│   ├── Cadastro/
│   │   ├── Cadastro.Domain.Tests/
│   │   └── Cadastro.Application.Tests/
│   ├── OrdemServico/
│   │   ├── OrdemServico.Domain.Tests/
│   │   └── OrdemServico.Application.Tests/
│   └── PecasInsumos/
│       ├── PecasInsumos.Domain.Tests/
│       └── PecasInsumos.Application.Tests/
└── IntegrationTests/
    ├── WebApplicationFactoryFixture.cs     (Testcontainers + Postgres)
    ├── Modules/
    │   ├── Cadastro/
    │   ├── OrdemServico/
    │   └── PecasInsumos/
    └── EventBus/                            (verifica que eventos atravessam módulos)
```

### 10.2 Divisão Domain.Tests × Application.Tests

**Domain.Tests** — testes puros, sem IO, sem mocks. Validam:

- Invariantes dos agregados.
- Transições de estado.
- Geração correta de domain events.
- Regras de VOs.

São os testes que **sustentam os 80% de cobertura do domínio crítico** exigidos pelo PDF, e são os mais rápidos (rodam em milissegundos).

**Application.Tests** — testes dos handlers, com mocks dos repositórios e ports ACL. Validam:

- Orquestração correta entre agregados e ports.
- Publicação de integration events quando esperado.
- Tradução correta de erros de domínio em resposta da camada Application.

### 10.3 IntegrationTests (projeto único)

Sobe a aplicação inteira com `WebApplicationFactory<Program>` + Postgres real via **Testcontainers.PostgreSql**. Testa endpoints ponta-a-ponta por módulo. Organizado em subpastas por BC, mas num único assembly (evita 3 factories/setup 3 vezes).

Inclui uma pasta `EventBus/` para testes que verificam **fluxo cross-module**: dispara ação em um módulo, verifica que o efeito em outro módulo aconteceu (ex.: aprovar orçamento → estoque decrementado).

**Por que banco real (Testcontainers) em vez de mock ou InMemory provider:** InMemory do EF Core não suporta schemas nem a maior parte de PostgreSQL real — passa teste, quebra em produção. Testcontainers sobe um Postgres real por sessão de teste, descartado ao final. Custo: ~1s de startup; benefício: teste fidedigno.

### 10.4 Stack de testes escolhida

- **xUnit** — framework.
- **Moq** — mocks (preferência pessoal sobre NSubstitute).
- **Asserts nativos do xUnit** — sem FluentAssertions.
- **Testcontainers.PostgreSql** — Postgres real em integração.
- **Coverlet** (já no template) + **ReportGenerator** — relatório de cobertura pra atingir e comprovar 80%.

---

## 11. Docker e execução local

- **Dockerfile** — mora em `src/Bootstrap/Api/`. Multi-stage: build no `sdk`, publish, runtime no `aspnet`.
- **docker-compose.yml** — na raiz, orquestra:
  - `postgres` (imagem `postgres:16`, volume persistente, variáveis de ambiente do banco).
  - `api` (build do Dockerfile, depende do postgres).
- **README.md** — instruções de `docker compose up`, como acessar Swagger, como rodar migrations, como rodar testes.

---

## 12. Resumo das decisões (referência rápida)

| Tema | Decisão |
|---|---|
| Arquitetura macro | Modular Monolith (1 módulo por BC) |
| Bounded Contexts | Cadastro, OrdemServico, PecasInsumos |
| Projetos por módulo | 5 (Domain, Application, Infrastructure, Presentation, Contracts) |
| Controllers | Por módulo em `<Modulo>.Presentation`, agregados via `AddApplicationPart` |
| Banco | PostgreSQL, 1 banco, schema por módulo, sem FK cross-schema |
| ORM | EF Core, 1 `DbContext` por módulo, migrations por módulo |
| Application style | CQRS com MediatR v12, vertical slice por caso de uso |
| Domain organization | Por agregado (não por tipo) |
| Comunicação síncrona | ACL: `<Modulo>.Contracts` + porta no Domain consumidor + adapter na Infrastructure |
| Comunicação assíncrona | `IIntegrationEventBus` dedicado, in-process no MVP |
| DI | Cada módulo expõe `AddXxxModule`, Bootstrap apenas compõe |
| SharedKernel | Dividido em `SharedKernel.Domain` e `SharedKernel.Application`; mínimo, cresce sob demanda |
| Testes | `<Modulo>.Domain.Tests` + `<Modulo>.Application.Tests` por módulo; `IntegrationTests` único; xUnit + Moq |
| Cobertura | 80% nos domínios críticos (Domain dos módulos) |
| Auth | JWT no Bootstrap |
| Docs API | Swagger no Bootstrap |

---

## Apêndice A — Architecture Tests (opcional, para estudo)

**O que são.** Testes automatizados que validam regras **de arquitetura** — não de comportamento do código, mas de seu formato: "quem referencia quem", "onde tal tipo pode aparecer", "que pasta pode usar EF Core". Escritos com libs como **NetArchTest.Rules** (free, MIT) ou **ArchUnitNET**.

**Por que são valiosos num modular monolith.** A fronteira entre BCs e entre camadas se sustenta em referências de projeto **e** em disciplina. O compilador cobre o nível de assembly; os architecture tests cobrem o nível de **regras internas** que o compilador não pega. Exemplos do que eles detectam:

- Uma classe de `OrdemServico.Application` que acidentalmente fez `using Cadastro.Domain` (referência de projeto impede, mas se alguém adicionar a referência "só pra resolver rápido", o teste reprova no CI).
- Um tipo em `<Modulo>.Domain` que referencia `Microsoft.EntityFrameworkCore` — viola DIP.
- Um `.Contracts` que expõe um `AggregateRoot` em vez de um DTO simples.
- Um controller fora de `<Modulo>.Presentation`.

**Exemplo (NetArchTest):**

```csharp
[Fact]
public void Domain_DeveSerIsoladoDeInfraestrutura()
{
    var result = Types.InAssembly(typeof(OrdemServico).Assembly)
        .ShouldNot()
        .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
        .GetResult();

    Assert.True(result.IsSuccessful);
}

[Fact]
public void ModuloOrdemServico_NaoReferenciaDomainDeOutrosModulos()
{
    var result = Types.InAssembly(typeof(OrdemServico).Assembly)
        .ShouldNot()
        .HaveDependencyOnAny("Cadastro.Domain", "PecasInsumos.Domain")
        .GetResult();

    Assert.True(result.IsSuccessful);
}
```

**Recomendação:** se sobrar tempo na Fase 1, adicione um projeto `tests/ArchitectureTests/` com ao menos essas duas regras. Tem ótimo apelo pra apresentação do trabalho ("eu não só modularizei — eu *enforço* a modularização no CI") e leva umas duas horas pra colocar de pé. Ficou aqui como extra consciente, pra não atrapalhar o MVP.

---

## Apêndice B — Ordem sugerida de implementação (não-obrigatória)

Quando for implementar, uma ordem que minimiza retrabalho:

1. **SharedKernel.Domain** (bases) + **SharedKernel.Application** (bus + behaviors).
2. **Bootstrap/Api** esqueleto (Program.cs mínimo, Swagger, health check).
3. **Cadastro** fim-a-fim (Domain → Application → Infrastructure → Presentation → Contracts → Tests), com 1 ou 2 casos de uso do CRUD de Cliente. Serve para validar toda a "pilha" antes de duplicar.
4. **docker-compose + Dockerfile** com Postgres e app rodando.
5. Os outros CRUDs de Cadastro (Veiculo, Servico).
6. **OrdemServico** fim-a-fim com o caso de uso `GerarOrdemServico` (já exercita ACL pra Cadastro).
7. **PecasInsumos** fim-a-fim com `BuscarPecas` e `AtualizarEstoque`.
8. Restante dos comandos de OS (Iniciar diagnóstico, Gerar orçamento, Aprovar, Executar, Finalizar, Concluir).
9. Integration events: `OrcamentoAprovado` → decrementar estoque; `OSFinalizada` → notificação (stub).
10. JWT nos endpoints administrativos.
11. Testes de integração e cobertura >= 80%.
12. (Opcional) Architecture tests.

---

_Este documento foi produzido em sessão de brainstorming estruturada. Alterações posteriores devem ser refletidas aqui; ele é a fonte única de verdade sobre a estrutura do projeto._
