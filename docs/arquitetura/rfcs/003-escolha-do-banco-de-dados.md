# RFC-003 — Escolha do banco de dados, modelo relacional e ajustes de consistência

- **Status:** Aprovado
- **Data:** 2026-09-10
- **Depende de:** [RFC-002](README.md) para a escolha do serviço gerenciado concreto (ainda
  pendente) — ver seção 3.2.
- **Atualização da Fase 4:** o PostgreSQL deixou de ser único — cada serviço tem a própria instância RDS
  (`cadastro`, `pecas-insumos`, `ordens-servico`), mantendo os mesmos schemas, e o serviço de Pagamento
  usa DynamoDB ([ADR-009](../adrs/009-decomposicao-em-microsservicos.md)).

---

## 1. Contexto e problema

O enunciado da Fase 3 permite PostgreSQL, MySQL, SQL Server "ou outro", mas exige que a escolha seja
**formalmente justificada**, com o modelo relacional, o diagrama entidade-relacionamento, os
relacionamentos entre entidades e os ajustes de consistência e desempenho documentados.

O código já roda sobre PostgreSQL desde a Fase 1: os três módulos (`Cadastro`, `OrdensServico`,
`PecasInsumos`) referenciam `Npgsql.EntityFrameworkCore.PostgreSQL` em seus `.csproj`, e as
migrations já geradas usam tipos e sintaxe específicos do Postgres (`uuid`, `timestamp with time
zone`, o operador de regex `~` na check constraint de `cliente.documento`). Esta RFC não está
escolhendo entre implementar do zero em um banco ou outro — está **formalizando por que a escolha
já feita é a correta**, com os mesmos critérios que se aplicariam se a decisão estivesse em aberto, e
documentando o modelo relacional resultante.

## 2. Alternativas consideradas

| Critério | PostgreSQL | MySQL | SQL Server |
|---|---|---|---|
| Schema como fronteira lógica dentro de **um único banco** | Nativo: `CREATE SCHEMA`, cada `DbContext` já chama `HasDefaultSchema` (`cadastro`, `ordem_servico`, `pecas_insumos`) mapeando 1:1 para os três bounded contexts | Não existe o conceito: em MySQL "schema" é sinônimo de "database" — simular 3 BCs isolados exigiria 3 bancos separados, quebrando o modelo de "um banco, três fronteiras lógicas" já implementado | Nativo, equivalente ao Postgres — não é um diferencial contra o Postgres |
| Tipo usado para `Id` (`Guid` no C#) | `uuid` nativo de 16 bytes, indexável e validado na escrita — é o tipo que já está em toda `id`/`*_id` das migrations | Sem tipo nativo de UUID: o provider (Pomelo) mapeia `Guid` para `CHAR(36)`, mais caro em espaço e índice, e sem ordenação sequencial | `uniqueidentifier` nativo — equivalente ao Postgres |
| Tipo usado para timestamps (`created_at`, `updated_at`, `status_alterado_em`, `data_aprovacao`, etc.) | `timestamp with time zone`, já em uso em toda coluna de data das três migrations — instante normalizado, sem ambiguidade de fuso | Não tem tipo timezone-aware real: `TIMESTAMP` converte para o fuso da sessão e tem limite de ano 2038; `DATETIME` não carrega fuso algum — a aplicação teria que garantir disciplina de UTC por conta própria | `datetimeoffset` nativo — equivalente ao Postgres |
| Constraint de validação já escrita | `CK_cliente_documento_digits` usa o operador de regex POSIX `~`, sintaxe Postgres | Reescrita necessária (`REGEXP_LIKE`, sintaxe diferente) | Reescrita necessária (não há operador de regex nativo em check constraint; exigiria função CLR ou `LIKE`) |
| Maturidade do provider EF Core | `Npgsql.EntityFrameworkCore.PostgreSQL`, mantido pela própria equipe Npgsql, já na versão `10.0.0` acompanhando EF Core 10/.NET 10 no mesmo ciclo de lançamento | `Pomelo.EntityFrameworkCore.MySql`, mantido pela comunidade, historicamente atrasado em relação a cada major do EF Core | `Microsoft.EntityFrameworkCore.SqlServer`, mantido pela Microsoft — maturidade equivalente ao Npgsql, não é um diferencial contra o Postgres |
| Custo de licença | Community, sem custo | Community, sem custo | Cobrança por instância/vCPU nas edições além do Express; Express tem teto de 10 GB por banco — um teto real para um serviço gerenciado de produção |
| Custo de migração a partir do estado atual | Nenhum — é o que já está implementado | Reescrever os três `DbContext`, todas as migrations e a check constraint | Reescrever os três `DbContext`, todas as migrations e a check constraint |

**MySQL** perde no primeiro critério: a ausência de um conceito de schema equivalente ao do Postgres
quebra exatamente a técnica que o projeto usa para simular isolamento de bounded contexts dentro de
um único banco (ver seção 4.1). **SQL Server** é tecnicamente equivalente ao Postgres em schemas e
providers, mas custa a reescrita de todo o código de acesso a dados já existente e carrega custo de
licença nas edições viáveis para produção, sem nenhum ganho compensatório.

## 3. Decisão

### 3.1 Tecnologia: PostgreSQL

Mantém-se PostgreSQL como SGBD, pelos argumentos da seção 2 — todos verificáveis diretamente nos
`.csproj` e nas migrations dos três módulos, não em características genéricas do produto.

### 3.2 Escopo explícito: tecnologia, não instância

Esta RFC decide **a tecnologia** (PostgreSQL) e **o requisito de o banco ser um serviço gerenciado**
(sem administração manual de patch/backup/HA pela equipe). Ela **não** decide qual serviço gerenciado
concreto hospeda esse Postgres — Amazon RDS, Cloud SQL ou equivalente depende do provedor de nuvem, e
essa escolha é o objeto do [RFC-002](README.md), que segue **pendente** por depender da avaliação da
conta AWS Academy disponível para o projeto. Quando o RFC-002 for resolvido, a instância concreta é
adicionada lá, referenciando esta RFC para a tecnologia.

## 4. Ajustes no modelo relacional para consistência e desempenho

Cada item abaixo existe de fato no código (configurações do EF Core e migrations) — nenhum é
proposta, todos já estão em produção.

### 4.1 Um schema por bounded context, sem FK entre schemas

Os três `DbContext` (`CadastroDbContext`, `OrdensServicoDbContext`, `PecasInsumosDbContext`) chamam
`modelBuilder.HasDefaultSchema(...)` para `cadastro`, `ordem_servico` e `pecas_insumos`,
respectivamente — um schema por módulo/bounded context, todos no mesmo banco físico.

Dentro de um schema, FK física é usada normalmente (ex.: `veiculo.cliente_id → cliente.id`). Entre
schemas, nenhuma FK é criada — as colunas que representam essas referências
(`ordem_servico.ordem_servico.cliente_id`, `.veiculo_id`, `os_servico.servico_id`,
`os_peca.peca_insumo_id`) são `uuid NOT NULL` simples, sem `REFERENCES`. Isso simula o isolamento que
existiria se cada bounded context fosse um serviço com seu próprio banco, e é o motivo por trás da
regra já registrada em [`database-schema.md`](../database-schema.md#regra-de-fk-entre-schemas).

**Custo assumido conscientemente:** o banco não impede a criação de uma OS com `cliente_id`
inexistente. Essa integridade referencial passa a ser responsabilidade da camada de Application, via
ACL (o gateway que resolve o cliente antes de aceitar o comando) — o mesmo modelo já usado e
documentado para a leitura de `cadastro.cliente` pela Lambda de autenticação no
[ADR-002](../adrs/002-lambda-le-o-banco-diretamente.md), só que aqui aplicado dentro do próprio
processo da aplicação em vez de entre serviços físicos.

### 4.2 FKs intra-schema com `ON DELETE CASCADE`

As quatro FKs físicas do projeto (`veiculo.cliente_id`, `os_servico.ordem_servico_id`,
`os_peca.ordem_servico_id`, `orcamento.ordem_servico_id`) foram todas geradas com
`onDelete: ReferentialAction.Cascade`. Isso garante que, se uma linha pai for removida (hoje isso não
acontece pelo fluxo normal da aplicação — cliente e serviço usam exclusão lógica, seção 4.5 — mas o
banco não depende disso para ficar consistente), nenhuma linha filha fica órfã: apagar uma
`ordem_servico` remove em cascata seus `os_servico`, `os_peca` e `orcamento`, e apagar um `cliente`
removeria seus `veiculo`. É um ajuste de consistência que não aparecia no `database-schema.md`
anterior (o DDL documentado usava `REFERENCES` sem especificar a ação), e foi corrigido nesta
revisão.

### 4.3 Índices únicos e índices de FK

Extraídos das migrations reais (`InitialCreate` de cada módulo):

| Índice | Tabela | Tipo | Consulta que serve |
|---|---|---|---|
| `IX_cliente_documento` | `cadastro.cliente` | Único | Login/consulta por CPF/CNPJ e a validação de duplicidade em `CadastrarCliente` |
| `IX_veiculo_placa` | `cadastro.veiculo` | Único | Validação de duplicidade em `CadastrarVeiculo` e busca de veículo por placa |
| `IX_veiculo_cliente_id` | `cadastro.veiculo` | FK (não único) | Listar os veículos de um cliente; suporte à FK `veiculo.cliente_id → cliente.id` |
| `IX_orcamento_ordem_servico_id` | `ordem_servico.orcamento` | FK (não único) | Carregar o(s) orçamento(s) de uma OS ao montar o agregado `OrdemServico` |
| `IX_os_servico_ordem_servico_id` | `ordem_servico.os_servico` | FK (não único) | Carregar os itens de serviço de uma OS ao montar o agregado |
| `IX_os_peca_ordem_servico_id` | `ordem_servico.os_peca` | FK (não único) | Carregar os itens de peça de uma OS ao montar o agregado |

Os índices de FK são criados automaticamente pelo EF Core para toda FK intra-schema declarada — não
há índice equivalente para as colunas cross-schema (`ordem_servico.cliente_id`,
`ordem_servico.veiculo_id`, `os_servico.servico_id`, `os_peca.peca_insumo_id`) porque, sem FK física,
o EF Core não as reconhece como relacionamento e não gera índice para elas. Isso é um ponto em aberto
de desempenho, não um ajuste já feito: se consultas por esses campos se tornarem frequentes, os
índices precisam ser criados manualmente.

### 4.4 Constraint de validação: `CK_cliente_documento_digits`

`ClienteConfiguration` declara uma check constraint diretamente no `ToTable`:

```csharp
builder.ToTable("cliente", t =>
    t.HasCheckConstraint("CK_cliente_documento_digits", "documento ~ '^[0-9]+$'"));
```

Ela garante, no próprio banco, que `documento` (CPF de 11 ou CNPJ de 14 dígitos) só contém dígitos —
sem máscara, sem espaços — independentemente de qual caminho de código grava a linha. A validação de
*tamanho* (11 ou 14) e de dígito verificador fica na Application/Domain, porque depende de lógica que
o SQL padrão não expressa bem; o banco cobre a garantia estrutural mais barata de checar toda vez.

### 4.5 Exclusão lógica (`ativo`)

`cliente`, `servico` e `peca_insumo` têm uma coluna `ativo BOOLEAN NOT NULL DEFAULT TRUE`, e os
comandos `DesativarCliente`, `DesativarServico` e `DesativarPecaInsumo` apenas viram essa flag para
`false` — nenhum `DELETE` é emitido. O motivo é duplo:

- **Referências existentes não podem quebrar.** Uma OS já criada referencia `cliente_id` e
  `veiculo_id` sem FK (seção 4.1), e um `os_servico`/`os_peca` referencia `servico_id`/`peca_insumo_id`
  do mesmo jeito. Apagar fisicamente a linha destruiria a capacidade de exibir o histórico de uma OS
  antiga (nome do cliente, descrição do serviço) mesmo sem FK apontando para ela.
- **Regra de negócio, não apenas infraestrutura.** Um cliente ou serviço "desativado" precisa parar de
  aparecer em cadastros/orçamentos novos, mas seu histórico continua válido — é exatamente a
  semântica de um flag, não de uma remoção.

`veiculo` e `ordem_servico` **não** têm essa coluna — não existe fluxo de "desativar" para eles no
domínio atual; a ausência é por não ter sido necessária, não por descuido.

### 4.6 `status_alterado_em` na ordem de serviço

Este é o ajuste de consistência mais recente do projeto (migration
`20260910020114_AdicionaStatusAlteradoEm`) e o melhor exemplo do enunciado "ajustes realizados para
garantir consistência".

A entidade `OrdemServico` sempre teve `AtualizadoEm`, um carimbo genérico reescrito por **todo**
método de transição de estado — inclusive os que não mudam `Status` (`RegistrarDiagnostico`,
`AprovarOrcamento`, `RejeitarOrcamento`, `NotificarCliente`). Isso é correto para "quando a linha foi
tocada pela última vez", mas errado para medir a duração de uma etapa do fluxo de uma OS.

O problema concreto, documentado em [`metricas.md`](../metricas.md#como-a-duração-é-derivada--e-a-limitação-que-restou):
a métrica `oficina.ordens_servico.etapa.duracao` para a etapa `finalizacao` precisa do intervalo entre
`Finalizar` e `Concluir`. Como `NotificarCliente` sempre roda entre essas duas transições e também
reescrevia `AtualizadoEm`, a métrica media, na prática, "notificação → entrega" — um viés sistemático
no caminho normal, não uma borda rara.

A correção foi adicionar `status_alterado_em`, reescrito **exclusivamente** pelos métodos que de fato
mudam `Status` (`IniciarDiagnostico`, `EnviarOrcamento`, `Executar`, `Finalizar`, `Concluir`, e a
construção via `AbrirComServicos`). `NotificarCliente` não muda `Status`, então não toca esse campo, e
a métrica de `finalizacao` passou a medir o intervalo real entre `Finalizar` e `Concluir`.

A migration ilustra também um ajuste de consistência sobre dados já existentes: a coluna foi criada
`nullable`, populada via `UPDATE ... SET status_alterado_em = updated_at` (a melhor aproximação
disponível no histórico, já que o campo não existia antes) e só então alterada para `NOT NULL` — o
padrão correto para adicionar uma coluna obrigatória a uma tabela com linhas existentes sem quebrar o
deploy.

## 5. Consequências

**Positivas**

- Nenhuma reescrita de código de acesso a dados: a decisão formaliza o que já roda.
- Tipos e constraints já usados (`uuid`, `timestamptz`, `numeric(10,2)`, check constraint de regex)
  continuam válidos sem adaptação.
- O modelo de "um schema por bounded context, sem FK cross-schema" continua sendo o mecanismo que
  simula isolamento de microsserviço dentro de um monólito modular, um objetivo explícito da
  arquitetura do projeto.

**Negativas / aceitas conscientemente**

- Integridade referencial cross-schema depende inteiramente da Application (seção 4.1) — um bug ali
  pode gravar uma OS apontando para um cliente inexistente sem o banco reclamar.
- Não há índice nas colunas cross-schema (seção 4.3) — se o volume de dados crescer e essas colunas
  entrarem em `WHERE`/`JOIN` feitos em memória pela Application, isso pode virar gargalo. Fica
  registrado como item de acompanhamento, não como problema já resolvido.
- A escolha do serviço gerenciado concreto (RDS, Cloud SQL, etc.) continua bloqueada pelo RFC-002.

## 6. Referências

- [`database-schema.md`](../database-schema.md) — DDL completo e diagrama ER atualizados nesta
  entrega.
- [`event-storming.md`](../event-storming.md) — origem das entidades e dos bounded contexts.
- [`metricas.md`](../metricas.md) — detalhe completo do problema e da correção de
  `status_alterado_em`.
- [ADR-002](../adrs/002-lambda-le-o-banco-diretamente.md) — outro caso de acesso cross-context ao
  banco, com a mesma lógica de exceção deliberada e isolada.
- [RFC-002](README.md) — escolha do provedor de nuvem e, com ela, do serviço gerenciado concreto.
