# RFC-002 — Escolha do provedor de nuvem

- **Status:** Aprovado
- **Data:** 2026-09-10
- **Decisões derivadas:** [ADR-006](../adrs/006-credenciais-de-nuvem-no-cicd.md)
- **Relacionado:** [RFC-003 — Escolha do banco de dados](003-escolha-do-banco-de-dados.md),
  [ADR-001](../adrs/001-jwt-hs256-segredo-compartilhado.md),
  [ADR-005](../adrs/005-quatro-repositorios-e-estrategia-de-branches.md)

---

## 1. Contexto

A Fase 3 exige que a solução rode em nuvem, com cinco componentes obrigatórios: **API Gateway**,
**Function Serverless**, **banco de dados gerenciado**, **cluster Kubernetes com escalabilidade** e
**Terraform** para provisionar tudo. O enunciado deixa a escolha do provedor livre — AWS, Azure,
Google Cloud ou outro que atenda aos requisitos.

A aplicação, a Function Serverless e a documentação arquitetural já estão implementadas e
integradas; o que falta é exatamente a camada de nuvem.

## 2. A restrição que domina a decisão

A conta disponível para este trabalho é uma **AWS Academy Learner Lab**, fornecida pela instituição.
Isso torna a comparação entre provedores em grande medida teórica: não há crédito institucional
equivalente em Azure ou Google Cloud, e custear a infraestrutura do próprio bolso não é uma
alternativa razoável para um trabalho acadêmico.

Portanto a pergunta relevante **não é** "qual provedor é o melhor", e sim **"a conta disponível
atende aos cinco requisitos obrigatórios?"**. A resposta é sim — e a seção 4 documenta a verificação.

## 3. Alternativas consideradas

**Azure.** Atende tecnicamente aos cinco requisitos (API Management, Functions, Azure Database for
PostgreSQL, AKS, Terraform). Descartada por ausência de conta institucional: exigiria conta pessoal
com cartão de crédito, e o AKS tem os mesmos custos de nós que a AWS, sem crédito para cobri-los.

**Google Cloud.** Idem (API Gateway, Cloud Functions, Cloud SQL, GKE). O GKE Autopilot tem um modelo
operacional interessante para quem trabalha sozinho, mas o argumento de custeio é o mesmo. O crédito
de trial gratuito é temporário e não sobrevive ao ciclo de correção do trabalho.

**AWS com conta pessoal.** Removeria as restrições da Academy descritas na seção 5 — principalmente
a impossibilidade de criar IAM roles. Descartada pelo custo: o control plane do EKS sozinho custa
cerca de US$ 0,10/hora, sem contar nós, NAT Gateway, RDS e balanceador.

**AWS Academy Learner Lab (escolhida).** Crédito institucional, todos os requisitos atendidos, e é o
ambiente que a instituição espera que seja usado.

## 4. Verificação de aderência aos requisitos

Verificado na lista oficial de serviços da conta (documento de referência do AWS Academy, arquivado
em [`docs/spec/aws-academy.pdf`](../../spec/aws-academy.pdf)):

| Requisito da fase | Serviço | Disponível? | Observações relevantes |
|---|---|---|---|
| API Gateway | **Amazon API Gateway** | ✅ | Assume a role `LabRole` |
| Function Serverless | **AWS Lambda** | ✅ | Usa `LabRole`; já implementada em `oficina-mecanica-lambda-auth` |
| Banco gerenciado | **Amazon RDS** | ✅ | PostgreSQL entre os engines suportados |
| Kubernetes com escalabilidade | **Amazon EKS** | ✅ | Roles pré-criadas `LabEksClusterRole` para cluster e nós |
| Terraform | — | ✅ | Nenhuma restrição de ferramenta; a restrição é de IAM (seção 5) |
| Registro de imagens | **Amazon ECR** | ✅ | `LabRole` com leitura; usuário do console com escrita |

Complementares também disponíveis e usados no desenho: **VPC**, **Elastic Load Balancing**,
**Secrets Manager**, **CloudWatch**, **EC2 Auto Scaling**.

> **Atualização — VPC default, não uma VPC criada.** Este RFC e o
> [diagrama de infraestrutura](../diagramas/infraestrutura.md) originalmente presumiam que
> `oficina-mecanica-infra-k8s` criaria sua própria VPC (subnets públicas/privadas, Internet
> Gateway, NAT Gateway) — é o desenho que o Terraform daquele repositório teve até ser
> simplificado. Uma auditoria contra os limites reais da conta mostrou que a Fase 3 exige EKS com
> escalabilidade, mas **não exige criar VPC**, e que a VPC default da conta já atende ao mínimo de
> 2 AZs que o EKS exige. O Terraform passou a usar a VPC default via `data` sources — sem criar
> VPC, subnets, Internet Gateway, NAT Gateway ou route tables — o que elimina o custo e o ponto de
> falha do NAT Gateway (ver
> [`oficina-mecanica-infra-k8s` PR #3](https://github.com/gabrielMauad/oficina-mecanica-infra-k8s/pull/3)).
>
> Consequência aceita: os nós do EKS ficam em subnet **pública** (IP público automático), em vez de
> subnet privada atrás do NAT. Isso não abre a aplicação para a internet — os security groups
> dedicados (não o SG default da VPC) continuam sendo a única proteção de tráfego, e a única
> entrada pública é o API Gateway. É uma troca deliberada de isolamento de rede por custo e
> simplicidade, aceitável para uma demonstração acadêmica; numa conta de produção real a VPC
> voltaria a ser criada, com os nós em subnet privada.

## 5. Decisão

**AWS**, na conta AWS Academy Learner Lab, com:

| Componente | Serviço | Dimensionamento |
|---|---|---|
| Cluster Kubernetes | **Amazon EKS** | Node group com instâncias até `large` (limite da conta) |
| Banco de dados | **Amazon RDS PostgreSQL** | Classe *burstable* até `medium`, `gp2` até 100 GB |
| Function Serverless | **AWS Lambda** | Runtime .NET 8 (ver README do repositório da Function) |
| Roteamento | **Amazon API Gateway** | HTTP API |
| Registro de imagens | **Amazon ECR** | Substitui o Docker Hub usado na Fase 2 |
| Região | **`us-east-1`** | A conta só permite `us-east-1` e `us-west-2` |

## 6. Consequências — as restrições reais da conta

Estas não são detalhes de implementação: elas mudam o que o Terraform pode declarar e como o
pipeline funciona. Ignorá-las produz código que falha no `apply`.

### 6.1 Não é possível criar IAM roles

A conta permite criar apenas *service-linked roles*. Não é possível criar usuários, grupos ou roles
comuns. Em contrapartida, existem roles pré-criadas: **`LabRole`** (uso geral, anexada a recursos) e
**`LabEksClusterRole`** (cluster e nós do EKS).

**Consequência para o Terraform:** nenhum recurso `aws_iam_role` nos módulos. As roles são
**referenciadas** com `data "aws_iam_role"` e seus ARNs passados aos recursos que as exigem
(`aws_eks_cluster.role_arn`, `aws_eks_node_group.node_role_arn`, `aws_lambda_function.role`).

Isso invalida a suposição inicial do plano da fase, que previa criar um IAM role para autenticação
OIDC do GitHub Actions — ver [ADR-006](../adrs/006-credenciais-de-nuvem-no-cicd.md).

### 6.2 Limites de dimensionamento

- **EKS / EC2:** instâncias até `large`, máximo de 32 vCPU e 9 instâncias simultâneas por região.
  Suficiente para demonstrar o HPA, que escala pods (1 a 5), não nós.
- **RDS:** apenas classes *burstable* até `medium`; armazenamento `gp2` até 100 GB, sem PIOPS;
  apenas On-Demand.
- **RDS — enhanced monitoring não é suportado.** É a pegadinha mais provável: vários módulos
  Terraform ligam essa opção por padrão. É preciso manter `monitoring_interval = 0` explicitamente.

### 6.3 Ciclo de vida dos recursos

O ambiente é efêmero por natureza: recursos podem ser parados ao fim de uma sessão, e **uma
instância RDS parada é religada automaticamente pela AWS após sete dias**. Isso reforça a estratégia
já prevista no plano: provisionar, validar, gravar a demonstração e **destruir**, usando o histórico
do GitHub Actions como evidência do deploy — e não manter o ambiente de pé indefinidamente.

### 6.4 Estado do Terraform

O state permanecerá em bucket **S3** (com lock em DynamoDB), como previsto. Nada na conta impede
isso — S3 e DynamoDB estão disponíveis. O bucket é criado uma vez, manualmente, antes do primeiro
`terraform init`, por ser pré-requisito do próprio backend.

## 7. Questões em aberto

- **Multi-AZ no RDS:** a lista de serviços da conta não menciona restrição, mas versões anteriores da
  documentação do Learner Lab a proibiam explicitamente. Como o desenho não depende de alta
  disponibilidade do banco para a demonstração, a instância será **single-AZ**; se Multi-AZ estiver
  disponível, é um incremento, não um pré-requisito.
- **Ferramenta de observabilidade:** segue pendente no RFC-004. A instrumentação já exporta por OTLP
  sem acoplamento a fornecedor ([ADR-004](../adrs/004-correlacao-via-traceid-w3c.md)), então a
  escolha não bloqueia o provisionamento.
