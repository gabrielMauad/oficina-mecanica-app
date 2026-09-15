# ADRs — Architecture Decision Records

Registro das decisões arquiteturais **permanentes** do projeto: aquelas que, uma vez tomadas,
condicionam o código e são caras de reverter.

## Formato

Cada ADR tem: contexto, decisão, alternativas consideradas e consequências (incluindo as negativas
e como são mitigadas). Uma ADR aceita **não é editada** quando muda de ideia — cria-se uma nova que
a substitui, e a antiga passa a `Substituída por ADR-XXX`.

## Relação com os RFCs

- **RFC** — análise aberta de um problema, com alternativas e trade-offs. Discute.
- **ADR** — o registro curto da decisão que saiu dessa análise. Conclui.

Quando uma decisão nasce de um RFC, a ADR referencia o RFC em vez de repetir a análise.
Ver [`../rfcs/`](../rfcs/).

## Índice

| ADR | Título | Status |
|---|---|---|
| [ADR-001](001-jwt-hs256-segredo-compartilhado.md) | JWT assinado em HS256 com segredo compartilhado | Aceita |
| [ADR-002](002-lambda-le-o-banco-diretamente.md) | A Lambda de autenticação lê o banco diretamente | Aceita |
| [ADR-003](003-dois-emissores-e-autorizacao-por-papel.md) | Dois emissores de token e autorização por papel | Aceita |
| [ADR-004](004-correlacao-via-traceid-w3c.md) | Correlação de requisições via `traceId` do W3C/OpenTelemetry | Aceita |
| [ADR-005](005-quatro-repositorios-e-estrategia-de-branches.md) | Quatro repositórios e estratégia de branches | Aceita |
| [ADR-006](006-credenciais-de-nuvem-no-cicd.md) | Credenciais de nuvem no CI/CD | Aceita |
| [ADR-007](007-padrao-de-comunicacao-entre-modulos.md) | Padrão de comunicação entre módulos | Aceita |
| [ADR-008](008-escalonamento-horizontal-com-hpa.md) | Escalonamento horizontal com HPA | Aceita |
