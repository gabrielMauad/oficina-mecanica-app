# ADR-011 — A Lambda de autenticação consulta o Cadastro por HTTP

- **Status:** Aceita
- **Data:** 2026-11-14
- **Substitui:** [ADR-002](002-lambda-le-o-banco-diretamente.md)
- **Contexto maior:** [RFC-001 — Estratégia de autenticação](../rfcs/001-estrategia-de-autenticacao.md)

## Contexto

A ADR-002 aceitou, como exceção deliberada ao isolamento entre contextos, que a Function de
autenticação por CPF lesse `cadastro.cliente` direto no PostgreSQL. Com o Cadastro como serviço
próprio, de banco próprio, essa leitura passa a ser exatamente o acesso ao banco de outro serviço
que a Fase 4 proíbe — e não existe mais um banco único que a Function possa consultar.

## Decisão

A Lambda **deixa de acessar banco** e consulta o serviço de Cadastro por HTTP:
`GET /api/v1/clientes/documento/{cpf}` (só dígitos; 200 com o mesmo ViewModel de
`GET /api/v1/clientes/{id}`, 404 se não existir).

- A chamada vai pela **NLB interna** (`http://<dns-da-nlb>:30082`, o listener do Cadastro), de dentro
  da VPC, sem passar pelo API Gateway.
- Como as rotas do Cadastro exigem o papel `Oficina`, a Function assina um **JWT de serviço** com o
  mesmo segredo compartilhado: `iss=oficina-mecanica-auth`, `aud=oficina-mecanica-api`,
  `sub=oficina-mecanica-lambda-auth`, `role=Oficina`, validade de 5 minutos.
- O token entregue ao cliente continua o mesmo: `role=Cliente`, `sub` = id do cliente.

## Alternativas consideradas

- **Manter a leitura direta do banco do Cadastro:** descartada — viola a fronteira de banco.
- **Replicar os clientes num banco da Lambda:** descartada — duplica dado e cria um problema de
  sincronização sem benefício.

## Consequências

- (+) Nenhum serviço acessa o banco de outro; o usuário de banco somente leitura previsto na ADR-002
  deixa de ser necessário.
- (−) A Lambda passa a depender da disponibilidade do Cadastro e da NLB interna.
- (−) A Function passa a assinar também um token de serviço e precisa alcançar a NLB pela VPC
  (sem NAT, sem internet).
