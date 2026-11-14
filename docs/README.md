# Documentação — Sistema de Oficina Mecânica

Índice da documentação do projeto. O ponto de entrada geral é o
[README principal](../README.md); esta pasta detalha cada tema.

> **Fase 4:** o sistema é composto por **cinco microsserviços** (um repositório cada); este repositório é
> o serviço **OrdensServico** e concentra a documentação geral — diagrama geral, justificativa da
> divisão, Saga, coleções e observabilidade. A documentação específica de cada outro serviço está no
> README do respectivo repositório (links no [README principal](../README.md#repositórios-do-projeto)).

## 🏛️ Arquitetura — [`arquitetura/`](arquitetura/)

| Documento | Conteúdo |
|---|---|
| [`diagramas/componentes.md`](arquitetura/diagramas/componentes.md) | **Desenho dos componentes** — arquitetura distribuída de cinco serviços (C4 níveis 1–4, incluindo implantação em nuvem, Mermaid) |
| [`diagramas/saga.md`](arquitetura/diagramas/saga.md) | **Saga coreografada** — fluxo completo da OS entre ordens-servico, pecas-insumos e pagamento, eventos, compensações e limitações (Mermaid) |
| [`diagramas/sequencia-abertura-os.md`](arquitetura/diagramas/sequencia-abertura-os.md) | **Diagrama de sequência** da abertura de uma OS — rotas simples e completa, pipeline MediatR, ACL sobre HTTP e publicação de eventos (Mermaid) |
| [`diagramas/infraestrutura.md`](arquitetura/diagramas/infraestrutura.md) | **Desenho da infraestrutura** — ambiente local (um `docker compose` por serviço + LocalStack e Jaeger) e ambiente de nuvem (AWS Academy: EKS, NLB, API Gateway, três RDS, DynamoDB, SNS/SQS, Lambda) (Mermaid) |
| [`diagramas/fluxo-deploy.md`](arquitetura/diagramas/fluxo-deploy.md) | **Desenho do fluxo de deploy** — uma pipeline por serviço (CI/CD, Mermaid) |
| [`diagramas/sequencia-autenticacao.md`](arquitetura/diagramas/sequencia-autenticacao.md) | **Sequência de autenticação** — cliente por CPF (Lambda consulta o Cadastro por HTTP) e oficina por e-mail/senha (serviço `autenticacao`) (Mermaid) |
| [`estrutura-do-projeto.md`](arquitetura/estrutura-do-projeto.md) | Decisões de estrutura, papel de cada projeto, regras de referência (descreve o monólito modular; cada módulo virou um serviço na Fase 4) |
| [`clean-architecture.md`](arquitetura/clean-architecture.md) | Análise de aderência à Clean Architecture (anéis, Regra de Dependência) |
| [`rfcs/`](arquitetura/rfcs/) | **RFCs** — análise das decisões técnicas relevantes, com alternativas e trade-offs |
| [`adrs/`](arquitetura/adrs/) | **ADRs** — registro das decisões arquiteturais permanentes (a Fase 4 acrescenta a [009 — decomposição em microsserviços](arquitetura/adrs/009-decomposicao-em-microsservicos.md), a [010 — Saga coreografada](arquitetura/adrs/010-saga-coreografada-com-masstransit.md) e a [011 — Lambda consulta o Cadastro por HTTP](arquitetura/adrs/011-lambda-consulta-o-cadastro-por-http.md)) |
| [`decisoes.md`](arquitetura/decisoes.md) | Decisões de design relevantes + ciclo de vida da OS (com o pagamento da Fase 4) |
| [`metricas.md`](arquitetura/metricas.md) | Contrato de métricas de negócio (meters, instrumentos, quem emite em cada serviço, limitações) |
| [`database-schema.md`](arquitetura/database-schema.md) | Schema dos bancos PostgreSQL com DDL, diagrama ER (Mermaid) e rastreabilidade event storming → coluna |
| [`event-storming.md`](arquitetura/event-storming.md) | Event storming com todos os fluxos e contextos delimitados |

## 📋 Planos — [`planos/`](planos/)

Planos de implementação e specs de refatoração (histórico de execução do projeto).

| Documento | Conteúdo |
|---|---|
| [`plano-de-tarefas.md`](planos/plano-de-tarefas.md) | Plano de tarefas do MVP (T01–T32) |
| [`refatoracao-clean-architecture/`](planos/refatoracao-clean-architecture/) | Planos da refatoração para Clean Architecture (00–07) |
| [`refatoracao-domain-events.md`](planos/refatoracao-domain-events.md) | Spec da refatoração dos domain events |
| [`infra-fase-2/`](planos/infra-fase-2/) | Plano da infraestrutura da Fase 2 (Docker, K8s, Terraform, CI/CD) |
| [`fase-3/`](planos/fase-3/) | Análise da spec da Fase 3 e divisão do trabalho |

## 🧪 Guias — [`guias/`](guias/)

| Documento | Conteúdo |
|---|---|
| [`teste-cenario-feliz.md`](guias/teste-cenario-feliz.md) | Guia E2E do happy path, inclusive o pagamento (curl + resultados esperados) |
| [`teste-cenarios-alternativos.md`](guias/teste-cenarios-alternativos.md) | Guia E2E de validações de erro, rejeição/estorno e pagamento recusado (compensação) |
| [`collection_bruno.yml`](guias/collection_bruno.yml) | Coleção [Bruno](https://usebruno.com) com os endpoints dos cinco serviços, a Lambda, o mock do Mercado Pago e o roteiro "Fluxo da Saga"; ambientes `Local` e `Nuvem` |
| [`collection_postman.json`](guias/collection_postman.json) | Mesma coleção em formato Postman (v2.1), com os ambientes [`Local`](guias/postman_environment_local.json) e [`Nuvem`](guias/postman_environment_nuvem.json) |

## 📈 Observabilidade — [`observabilidade/`](observabilidade/)

| Documento | Conteúdo |
|---|---|
| [`dashboard-oficina-mecanica.json`](observabilidade/dashboard-oficina-mecanica.json) | Dashboard do New Relic importável: visão por serviço, métricas de OS e visão da Saga |
| [`alertas.md`](observabilidade/alertas.md) | Condições de alerta em NRQL, por serviço |
| [`uptime.md`](observabilidade/uptime.md) | Monitores sintéticos, um por serviço |
| [`validacao.md`](observabilidade/validacao.md) | Validação das consultas com dados reais e o que a Fase 4 ainda precisa conferir |

## 📎 Referência

| Pasta / Documento | Conteúdo |
|---|---|
| [`spec/`](spec/) | Enunciados oficiais FIAP/SOAT (PDFs das Fases 1, 2 e 3) e a lista de serviços da conta AWS Academy (`aws-academy.pdf`), referência do [RFC-002](arquitetura/rfcs/002-escolha-do-provedor-de-nuvem.md) |
| [`images/`](images/) | Imagens (desenhos C4 das fases anteriores e relatórios de cobertura) |

A pasta `infra/` (Terraform do cluster **kind** local da Fase 2) foi removida deste repositório —
a partir da Fase 3 o Terraform vive em repositórios dedicados:
[`oficina-mecanica-infra-k8s`](https://github.com/gabrielMauad/oficina-mecanica-infra-k8s),
[`oficina-mecanica-infra-db`](https://github.com/gabrielMauad/oficina-mecanica-infra-db) e
[`oficina-mecanica-lambda-auth`](https://github.com/gabrielMauad/oficina-mecanica-lambda-auth).
