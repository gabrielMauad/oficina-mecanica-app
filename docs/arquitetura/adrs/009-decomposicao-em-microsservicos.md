# ADR-009 — Decomposição em cinco microsserviços

- **Status:** Aceita
- **Data:** 2026-11-14
- **Relacionada:** [ADR-005](005-quatro-repositorios-e-estrategia-de-branches.md) (repositórios de
  infraestrutura e da Lambda, que continuam existindo)

## Contexto

Até a Fase 3 o sistema era um _Modular Monolith_: quatro Bounded Contexts (Cadastro, PecasInsumos,
OrdensServico, Autenticacao) com fronteiras físicas — um projeto por camada por módulo, sem
referência entre Domain/Application de módulos diferentes, um schema por módulo e nenhuma FK entre
schemas — rodando em um único processo e uma única imagem.

A Fase 4 exige, no mínimo: três microsserviços em repositórios próprios; banco próprio para cada
serviço que persiste dados, sem acesso cruzado; pelo menos um banco relacional **e** um não
relacional; mensageria assíncrona; e etapa de pagamento no fluxo da OS (Mercado Pago ou mock).
A divisão em si é livre: os exemplos do enunciado (OS / Billing / Execução e Produção) são
sugestões.

## Decisão

**Um serviço por Bounded Context do monólito, mais um serviço novo de Pagamento:**

| Serviço | Repositório | Banco |
|---|---|---|
| `ordens-servico` | `oficina-mecanica-app` (o antigo monólito) | PostgreSQL próprio |
| `cadastro` | `oficina-mecanica-cadastro` | PostgreSQL próprio |
| `pecas-insumos` | `oficina-mecanica-pecas-insumos` | PostgreSQL próprio |
| `pagamento` | `oficina-mecanica-pagamento` | **DynamoDB** |
| `autenticacao` | `oficina-mecanica-autenticacao` | nenhum |

- **A fronteira já existia.** Cada módulo vira um serviço movendo os seus projetos para um
  repositório novo; Domain e Application não mudam. O que muda são os adapters: as consultas entre
  módulos (`*.Contracts.Queries`) viram clientes HTTP e o barramento em memória vira mensageria
  ([ADR-010](010-saga-coreografada-com-masstransit.md)). Dividir por outro critério (por exemplo,
  Billing e Execução como serviços) obrigaria a reescrever essas fronteiras.
- **Pagamento é um serviço novo** porque o fluxo da OS não tinha pagamento e porque ele é o lugar
  natural do banco não relacional: registros de pagamento são acessados por chave
  (`OrdemServicoId` + `OrcamentoId`), sem junções, o que o DynamoDB atende sem refatorar nenhum
  banco SQL existente.
- **Autenticacao não tem banco.** O login da oficina é simulado: a credencial do administrador vem
  de configuração, então não há dado a persistir. A regra do enunciado pede banco próprio para quem
  persiste dados; não pede banco para quem não persiste.
- **PostgreSQL por serviço, uma instância RDS cada** (`for_each` no repositório
  `oficina-mecanica-infra-db`, segredo próprio no Secrets Manager). O isolamento fica garantido pela
  infraestrutura, sem depender de criar databases e usuários por SQL numa rede privada.
- **`SharedKernel` e o projeto de contratos de mensagens são duplicados em cada repositório**, e os
  contratos mantêm exatamente o mesmo namespace e os mesmos nomes de tipo, porque o MassTransit roteia
  pelo nome completo do tipo.
- **Autenticação entre serviços:** JWT HS256 com segredo compartilhado ([ADR-001](001-jwt-hs256-segredo-compartilhado.md)),
  validado por cada serviço. Chamadas HTTP entre serviços repassam o token recebido; consumidores de
  fila não fazem chamadas HTTP — os eventos carregam o que o consumidor precisa.
- **Roteamento:** o API Gateway roteia por prefixo de caminho para um listener da NLB por serviço
  (porta = NodePort do serviço).

## Alternativas consideradas

- **OrdensServico / Billing / Execução e Produção** (exemplo do enunciado): descartada. Execução e
  Produção não tem regra de negócio própria no código — é um status da OS —, e a divisão exigiria
  quebrar o Bounded Context de OrdensServico.
- **Mover um módulo SQL existente para NoSQL** (por exemplo, o usuário do Autenticacao ou o
  Cadastro): descartada. Reescreve persistência que já funciona e, no caso do Cadastro, quebraria a
  Lambda de autenticação por CPF.
- **Uma única instância RDS com um database por serviço:** descartada. O isolamento seria parcial
  (mesma instância, mesmo ciclo de vida) e exigiria bootstrap de databases e usuários por SQL.
- **Contratos e SharedKernel como pacote NuGet compartilhado:** fica como evolução futura. A cópia
  idêntica dispensa um feed de pacotes e mantém cada repositório autossuficiente.

## Consequências

- (+) Cada serviço tem código, imagem, pipeline, manifestos e banco próprios e é implantado de forma
  independente.
- (+) Clean Architecture preservada em cada serviço: nada em Domain/Application conhece HTTP,
  MassTransit, AWS SDK ou EF Core.
- (−) A duplicação do `SharedKernel` e dos contratos exige disciplina: uma divergência de namespace
  ou de nome de tipo entre repositórios faz a mensagem simplesmente não chegar. Mitigação: o projeto
  de contratos é copiado por inteiro mesmo nos serviços que não usam todos os eventos.
- (−) Cinco pipelines e três instâncias RDS em vez de uma de cada.
- (−) O fluxo da OS deixa de ser transacional — a consistência passa a depender da Saga.
