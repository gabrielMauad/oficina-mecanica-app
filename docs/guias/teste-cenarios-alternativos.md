# Guia de Testes E2E — Cenários Alternativos

> **Pré-requisito:** Ter executado pelo menos os passos 1–5 do [`teste-cenario-feliz.md`](teste-cenario-feliz.md)
> para ter `{CLIENTE_ID}`, `{VEICULO_ID}`, `{SERVICO_ID}` e `{PECA_ID}` disponíveis.
> Base URL da OS: `http://localhost:8080`; os demais serviços usam as portas indicadas abaixo.
>
> **Fase 4 — serviços separados.** Cada módulo roda como um serviço, em uma porta própria
> (`docker compose up` de cada repositório, mais `docker-compose.infra.yml` — ver o
> [README](../../README.md#execução-local)): **autenticacao** `8081` (login), **cadastro** `8082`
> (clientes, veículos, serviços), **pecas-insumos** `8083`, **ordens-servico** `8080` e **pagamento**
> `8084`. As URLs deste guia já usam essas portas; na nuvem, todas passam a ser o endpoint do
> API Gateway. A baixa e o estorno de estoque são **assíncronos** (eventos): depois de gerar ou
> rejeitar um orçamento, aguarde alguns segundos antes de conferir o estoque. A rota
> `PATCH /ordens-servico/{id}/executar` **não existe mais**: a OS entra em execução quando o
> **pagamento é aprovado** (Passo 11 do cenário feliz).
>
> **Tokens:** este guia assume as variáveis `$TOKEN_OFICINA` e `$TOKEN_CLIENTE` já exportadas —
> veja o **Passo 0** do [`teste-cenario-feliz.md`](teste-cenario-feliz.md#passo-0--obter-os-tokens).

---

## Tokens usados neste guia

A autorização é por papel (ver [RFC-001 §4.2](../arquitetura/rfcs/001-estrategia-de-autenticacao.md)).
Todos os cenários abaixo usam `$TOKEN_OFICINA`, **exceto** os que aprovam, rejeitam ou pagam o
orçamento (A11, A12, A13c e A21), que exigem `$TOKEN_CLIENTE`.

| Rota usada aqui | Token |
|---|---|
| `PATCH /ordens-servico/{id}/aprovar-orcamento` (A11) | `$TOKEN_CLIENTE` (papel `Cliente`) |
| `PATCH /ordens-servico/{id}/rejeitar-orcamento` (A13c) | `$TOKEN_CLIENTE` (papel `Cliente`) |
| `POST /pagamentos/ordens-servico/{id}/pagar` (A12, A21) | `$TOKEN_CLIENTE` (papel `Cliente`) |
| Todas as demais | `$TOKEN_OFICINA` (papel `Oficina`) |

> ⚠️ Nos dois cenários com `$TOKEN_CLIENTE`, o `sub` do token precisa ser o **dono da OS**
> (`{CLIENTE_ID}`). Se for outro cliente, a resposta é **403 Forbidden** — e não o 422 ou 200
> que o cenário espera, o que faz o teste parecer falhar por outro motivo.
>
> Em ambiente local a Function Serverless que emite o token de cliente não roda, então esse token
> é obtido manualmente (ver Passo 0 do guia do cenário feliz).

---

## Índice de Cenários

| # | Cenário | Módulo | HTTP esperado |
|---|---|---|---|
| A1 | CPF inválido no cadastro de cliente | Cadastro | 422 |
| A2 | CNPJ inválido no cadastro de cliente | Cadastro | 422 |
| A3 | Placa fora dos formatos aceitos | Cadastro | 422 |
| A4 | Documento duplicado | Cadastro | 422 |
| A5 | Placa duplicada | Cadastro | 422 |
| A6 | Criar OS com cliente inexistente | OrdemServico | 422 |
| A7 | Criar OS com veículo de outro cliente | OrdemServico | 422 |
| A8 | Registrar diagnóstico com peça sem estoque suficiente | OrdemServico | 422 |
| A9 | Registrar diagnóstico com serviço inexistente | OrdemServico | 422 |
| A10 | Registrar diagnóstico sem itens de peças | OrdemServico | 422 |
| A11 | Transição de estado inválida (pular etapa) | OrdemServico | 422 |
| A12 | Pagar sem pagamento pendente | Pagamento | 404 |
| A13 | **Rejeição de orçamento + estorno de estoque** | OrdemServico + PecasInsumos | 200 + estorno |
| A14 | Concluir OS sem notificação ao cliente | OrdemServico | 422 |
| A15 | Decrementar estoque além do disponível | PecasInsumos | 422 |
| A16 | Buscar entidade inexistente | Todos | 404 |
| A17 | Abrir OS completa (`/completa`) com cliente inexistente | OrdemServico | 422 |
| A18 | Abrir OS completa (`/completa`) sem peças | OrdemServico | 422 |
| A19 | Abrir OS completa (`/completa`) com peça indisponível | OrdemServico + PecasInsumos | 422 |
| A20 | Consultar status (`/{id}/status`) de OS inexistente | OrdemServico | 404 |
| A21 | **Pagamento recusado (`OTHE`) + compensação** | Pagamento + OrdemServico + PecasInsumos | 200 + orçamento rejeitado + estorno |

---

## A1 — CPF Inválido no Cadastro de Cliente

```bash
curl -s -X POST http://localhost:8082/api/v1/clientes \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "Fulano Inválido",
    "documento": "111.111.111-11",
    "email": "fulano@email.com",
    "telefone": "(31) 99999-0001",
    "pessoaFisica": true
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "...",
  "description": "CPF inválido."
}
```

> ✅ O value object `Cpf` valida o dígito verificador. Dígitos repetidos (111.111.111-11)
> são inválidos por qualquer algoritmo de CPF.

---

## A2 — CNPJ Inválido no Cadastro de Cliente

```bash
curl -s -X POST http://localhost:8082/api/v1/clientes \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "Empresa Inválida Ltda",
    "documento": "11.111.111/0001-11",
    "email": "empresa@email.com",
    "telefone": "(31) 3333-0001",
    "pessoaFisica": false
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "...",
  "description": "CNPJ inválido."
}
```

---

## A3 — Placa Fora dos Formatos Aceitos

Testa uma placa que não é nem Mercosul (`ABC1D23`) nem padrão antigo (`ABC-1234`).

```bash
curl -s -X POST http://localhost:8082/api/v1/veiculos \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "placa": "PLACA99",
    "modelo": "Gol",
    "marca": "Volkswagen",
    "ano": 2020,
    "clienteId": "{CLIENTE_ID}"
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "...",
  "description": "Placa inválida."
}
```

> ✅ Formatos válidos: `ABC1D23` (Mercosul) ou `ABC-1234` (antigo). `PLACA99` não casa com nenhum.

---

## A4 — Documento Duplicado (Cliente)

Tenta cadastrar um segundo cliente com o mesmo CPF do passo 1 do cenário feliz.

```bash
curl -s -X POST http://localhost:8082/api/v1/clientes \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "João Silva Cópia",
    "documento": "529.982.247-25",
    "email": "joao.copia@email.com",
    "telefone": "(31) 99999-0002",
    "pessoaFisica": true
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "Cliente.DocumentoJaExiste",
  "description": "Já existe um cliente com este documento."
}
```

---

## A5 — Placa Duplicada (Veículo)

Tenta cadastrar um segundo veículo com a mesma placa `ABC1D23` do cenário feliz.

```bash
curl -s -X POST http://localhost:8082/api/v1/veiculos \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "placa": "ABC1D23",
    "modelo": "HB20",
    "marca": "Hyundai",
    "ano": 2022,
    "clienteId": "{CLIENTE_ID}"
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "Veiculo.PlacaJaExiste",
  "description": "Já existe um veículo com esta placa."
}
```

---

## A6 — Criar OS com Cliente Inexistente

```bash
curl -s -X POST http://localhost:8080/api/v1/ordens-servico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "clienteId": "00000000-0000-0000-0000-000000000001",
    "veiculoId": "{VEICULO_ID}"
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "OrdemServico.ClienteInexistenteOuInativo",
  "description": "O cliente informado não existe ou está inativo."
}
```

> ✅ O ACL adapter `ClienteInfoAdapter` verifica se o cliente existe e está ativo
> antes de criar a OS.

---

## A7 — Criar OS com Veículo que Não Pertence ao Cliente

Crie um segundo cliente e tente abrir OS usando o veículo do primeiro cliente.

**7a — Criar segundo cliente:**
```bash
curl -s -X POST http://localhost:8082/api/v1/clientes \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "Maria Souza",
    "documento": "275.484.936-08",
    "email": "maria.souza@email.com",
    "telefone": "(31) 99999-0099",
    "pessoaFisica": true
  }' | jq '.clienteId'
```

> ⚙️ **Salve:** `CLIENTE2_ID = clienteId`

**7b — Tentar criar OS com veículo do cliente 1:**
```bash
curl -s -X POST http://localhost:8080/api/v1/ordens-servico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "clienteId": "{CLIENTE2_ID}",
    "veiculoId": "{VEICULO_ID}"
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "OrdemServico.VeiculoInexistenteOuNaoPertenceAoCliente",
  "description": "O veículo informado não existe ou não pertence ao cliente."
}
```

---

## A8 — Registrar Diagnóstico com Peça Sem Estoque Suficiente

**Pré-requisito:** Criar uma nova OS (passo 6 do cenário feliz) e iniciá-la (passo 7).
Depois crie uma peça com estoque zero e tente usá-la no diagnóstico.

**8a — Criar peça com estoque zero:**
```bash
curl -s -X POST http://localhost:8083/api/v1/pecas-insumos \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "Pastilha de Freio",
    "descricao": "Pastilha dianteira",
    "preco": 89.90,
    "quantidadeEmEstoque": 0,
    "unidadeDeMedida": "Par"
  }' | jq '.pecaInsumoId'
```

> ⚙️ **Salve:** `PECA_SEM_ESTOQUE_ID = pecaInsumoId`

**8b — Criar e iniciar nova OS:**
```bash
OS2_ID=$(curl -s -X POST http://localhost:8080/api/v1/ordens-servico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{"clienteId": "{CLIENTE_ID}", "veiculoId": "{VEICULO_ID}"}' \
  | jq -r '.id')

curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/$OS2_ID/iniciar-diagnostico
```

**8c — Tentar registrar diagnóstico com peça sem estoque:**
```bash
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/$OS2_ID/registrar-diagnostico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "descricaoDiagnostico": "Freios desgastados.",
    "servicos": [
      { "servicoId": "{SERVICO_ID}", "quantidade": 1 }
    ],
    "pecas": [
      { "pecaInsumoId": "{PECA_SEM_ESTOQUE_ID}", "quantidade": 2 }
    ]
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "OrdemServico.PecaIndisponivel",
  "description": "Peça/insumo indisponível para a quantidade informada."
}
```

> ✅ A verificação de disponibilidade via `IPecaDisponibilidadePort` (ACL) ocorre antes
> de tocar o agregado. Nenhuma OS foi modificada.

---

## A9 — Registrar Diagnóstico com Serviço Inexistente

```bash
# Usando a OS2 do cenário anterior (já em EmDiagnostico)
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/$OS2_ID/registrar-diagnostico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "descricaoDiagnostico": "Diagnóstico teste.",
    "servicos": [
      { "servicoId": "00000000-0000-0000-0000-000000000099", "quantidade": 1 }
    ],
    "pecas": [
      { "pecaInsumoId": "{PECA_ID}", "quantidade": 1 }
    ]
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "OrdemServico.ServicoNaoEncontrado",
  "description": "Servico não encontrado."
}
```

---

## A10 — Registrar Diagnóstico Sem Itens de Peças

O domínio exige pelo menos 1 serviço e 1 peça para gerar orçamento.

```bash
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/$OS2_ID/registrar-diagnostico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "descricaoDiagnostico": "Apenas serviço, sem peças.",
    "servicos": [
      { "servicoId": "{SERVICO_ID}", "quantidade": 1 }
    ],
    "pecas": []
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "OrdemServico.OrcamentoSemPecas",
  "description": "Não é possível gerar orçamento sem itens de peças."
}
```

---

## A11 — Transição de Estado Inválida (Pular Etapa)

> 🔑 **Token:** `$TOKEN_OFICINA` para criar a OS; `$TOKEN_CLIENTE` (dono da OS) para aprovar.

Tenta aprovar orçamento numa OS que ainda está em `Recebida` (sem passar pelo diagnóstico).

```bash
# Criar OS nova — ainda em Recebida
OS3_ID=$(curl -s -X POST http://localhost:8080/api/v1/ordens-servico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{"clienteId": "{CLIENTE_ID}", "veiculoId": "{VEICULO_ID}"}' \
  | jq -r '.id')

# Tentar aprovar sem passar pelas etapas anteriores
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/$OS3_ID/aprovar-orcamento \
  -H "Authorization: Bearer $TOKEN_CLIENTE" \
  | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "OrdemServico.TransicaoInvalida",
  "description": "Ordem de Serviço só pode aprovar orçamento quando está Aguardando Aprovação."
}
```

> ✅ Testes adicionais de transição inválida:
> - `PATCH /finalizar` em status `AguardandoAprovacao` → 422
> - `PATCH /concluir` em status `EmExecucao` (sem finalizar) → 422

---

## A12 — Pagar sem Pagamento Pendente

> 🔑 **Token:** `$TOKEN_CLIENTE` (papel `Cliente`).

A execução da OS só acontece pelo evento de pagamento aprovado, e o pagamento `Pendente` só existe
depois que o cliente aprova o orçamento. Tentar pagar uma OS cujo orçamento ainda não foi aprovado
não encontra pagamento para cobrar:

```bash
# Use OS3_ID do cenário A11 (OS em AguardandoAprovacao, orçamento Enviado, ainda não aprovado)
curl -s -X POST http://localhost:8084/api/v1/pagamentos/ordens-servico/$OS3_ID/pagar \
  -H "Authorization: Bearer $TOKEN_CLIENTE" \
  -H "Content-Type: application/json" \
  -d '{"titularCartao": "APRO"}' | jq .
```

**Resultado esperado:** `404 Not Found` (não há pagamento `Pendente` para a OS). A OS permanece
`AguardandoAprovacao`. O mesmo `404` ocorre ao pagar de novo uma OS cujo pagamento já foi resolvido
(aprovado, recusado ou cancelado).

---

## A13 — Rejeição de Orçamento com Estorno de Estoque ⭐

> 🔑 **Token:** `$TOKEN_OFICINA` em todos os subpassos, **exceto 13c** (rejeitar orçamento),
> que exige `$TOKEN_CLIENTE`.

**Este é o cenário alternativo mais importante.** Valida a cadeia completa de
eventos de rejeição: `RejeitarOrcamento` → domain event `OrcamentoRejeitado` →
integration event `OrcamentoRejeitadoIntegrationEvent` → estorno em PecasInsumos.

### Preparação — verificar estoque antes

```bash
curl -s http://localhost:8083/api/v1/pecas-insumos/{PECA_ID} | jq '.quantidadeEstoque'
```

> 📝 Anote o valor atual (deve ser **8** se veio do cenário feliz, ou **10** se é início).

### 13a — Criar nova OS e levá-la até AguardandoAprovacao

```bash
# Criar OS
OS_REJEICAO_ID=$(curl -s -X POST http://localhost:8080/api/v1/ordens-servico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{"clienteId": "{CLIENTE_ID}", "veiculoId": "{VEICULO_ID}"}' \
  | jq -r '.id')

echo "OS de rejeição: $OS_REJEICAO_ID"

# Iniciar diagnóstico
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/$OS_REJEICAO_ID/iniciar-diagnostico

# Registrar diagnóstico com 3 peças
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/$OS_REJEICAO_ID/registrar-diagnostico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "descricaoDiagnostico": "Revisão geral — cliente vai avaliar orçamento.",
    "servicos": [
      { "servicoId": "{SERVICO_ID}", "quantidade": 1 }
    ],
    "pecas": [
      { "pecaInsumoId": "{PECA_ID}", "quantidade": 3 }
    ]
  }' | jq '{status, orcamentos}'
```

### 13b — Verificar estoque decrementado (antes da rejeição)

```bash
curl -s http://localhost:8083/api/v1/pecas-insumos/{PECA_ID} | jq '.quantidadeEstoque'
```

> ✅ Estoque deve ter diminuído **3 unidades** (decremento automático via integration event).

### 13c — Rejeitar orçamento

> 🔑 **Token:** `$TOKEN_CLIENTE` (papel `Cliente`, `sub` = `{CLIENTE_ID}` dono da OS).

```bash
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/$OS_REJEICAO_ID/rejeitar-orcamento \
  -H "Authorization: Bearer $TOKEN_CLIENTE" \
  | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "id": "...",
  "status": "AguardandoAprovacao",
  "orcamentos": [
    {
      "status": "Rejeitado"
    }
  ]
}
```

### 13d — Verificar estoque estornado (após a rejeição)

```bash
curl -s http://localhost:8083/api/v1/pecas-insumos/{PECA_ID} | jq '.quantidadeEstoque'
```

**Resultado esperado:** estoque voltou ao valor anterior à geração do orçamento.

> ✅ O domain event `OrcamentoRejeitado` → integration event
> `OrcamentoRejeitadoIntegrationEvent` → handler `IncrementarEstoqueQuandoOrcamentoRejeitado`
> (no serviço PecasInsumos) devolveu as 3 unidades ao estoque. O estorno é assíncrono: aguarde alguns
> segundos antes de conferir.

### 13e — Verificar OS após rejeição

```bash
curl -s http://localhost:8080/api/v1/ordens-servico/$OS_REJEICAO_ID \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
```

> ✅ Status permanece `AguardandoAprovacao`. O orçamento rejeitado permite
> reabrir o diagnóstico (novo `registrar-diagnostico` é aceito pelo domínio).

---

## A14 — Concluir OS Sem Notificação ao Cliente

O domínio exige `notificado_em != null` para permitir a conclusão.
Tenta concluir uma OS que está `Finalizada` mas cujo `NotificarCliente` ainda não rodou.

> **Nota:** No fluxo atual, `NotificarCliente` é chamado automaticamente pelo handler
> `NotificarClienteAoFinalizar` logo após `Finalizar`. Este cenário seria acionado
> apenas se o handler falhasse. Para simulá-lo diretamente, pode-se:
>
> 1. Verificar que `GET /ordens-servico/{id}` mostra `notificadoEm != null` após `finalizar`
> 2. Confirmar que a regra existe no domínio pelo teste unitário (ver `OrdemServico.cs`)

A regra de negócio no domínio (`Concluir`) é:

```csharp
if (Status != StatusOrdemServico.Finalizada || NotificadoEm == null)
    return Error.Validation("OrdemServico.TransicaoInvalida",
        "Ordem de Serviço só pode ser concluída quando está Finalizada e o cliente foi notificado.");
```

---

## A15 — Decrementar Estoque Além do Disponível

```bash
# Tenta decrementar mais do que há em estoque
curl -s -X PATCH http://localhost:8083/api/v1/pecas-insumos/{PECA_ID}/estoque/saida \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "quantidade": 9999
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "PecaInsumo.EstoqueInsuficiente",
  "description": "Quantidade em estoque insuficiente para a operação."
}
```

> ✅ A regra de negócio do agregado `PecaInsumo` impede estoque negativo.

---

## A16 — Buscar Entidades Inexistentes (404)

**Cliente inexistente:**
```bash
curl -s http://localhost:8082/api/v1/clientes/00000000-0000-0000-0000-000000000099 \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
# Esperado: 404
```

**Veículo inexistente:**
```bash
curl -s http://localhost:8082/api/v1/veiculos/00000000-0000-0000-0000-000000000099 \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
# Esperado: 404
```

**Serviço inexistente:**
```bash
curl -s http://localhost:8082/api/v1/servicos/00000000-0000-0000-0000-000000000099 \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
# Esperado: 404
```

**Peça/Insumo inexistente:**
```bash
curl -s http://localhost:8083/api/v1/pecas-insumos/00000000-0000-0000-0000-000000000099 \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
# Esperado: 404
```

**OS inexistente:**
```bash
curl -s http://localhost:8080/api/v1/ordens-servico/00000000-0000-0000-0000-000000000099 \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
# Esperado: 404
```

---

## A17 — Abrir OS Completa com Cliente Inexistente

O endpoint `POST /completa` roda as mesmas validações de ACL do endpoint `Gerar` antes
de montar o orçamento.

```bash
curl -s -X POST http://localhost:8080/api/v1/ordens-servico/completa \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "clienteId": "00000000-0000-0000-0000-000000000001",
    "veiculoId": "{VEICULO_ID}",
    "servicos": [
      { "servicoId": "{SERVICO_ID}", "quantidade": 1 }
    ],
    "pecas": [
      { "pecaInsumoId": "{PECA_ID}", "quantidade": 1 }
    ]
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "OrdemServico.ClienteInexistenteOuInativo",
  "description": "O cliente informado não existe ou está inativo."
}
```

> ✅ Nenhuma OS é criada — a validação de cliente ocorre antes de montar orçamento ou
> reservar estoque.

---

## A18 — Abrir OS Completa Sem Peças

Diferente do fluxo manual (`registrar-diagnostico`), aqui a ausência de peças é barrada
já no `AbrirOrdemServicoCompletaValidator` (FluentValidation), antes mesmo de chegar ao
domínio.

```bash
curl -s -X POST http://localhost:8080/api/v1/ordens-servico/completa \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "clienteId": "{CLIENTE_ID}",
    "veiculoId": "{VEICULO_ID}",
    "servicos": [
      { "servicoId": "{SERVICO_ID}", "quantidade": 1 }
    ],
    "pecas": []
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "validation.failed",
  "description": "Ao menos uma peça deve ser informada."
}
```

> ✅ O mesmo vale para `servicos: []` (mensagem "Ao menos um serviço deve ser
> informado."), `servicoId`/`pecaInsumoId` vazios e `quantidade <= 0` em qualquer item.

---

## A19 — Abrir OS Completa com Peça Indisponível

```bash
# Criar peça com estoque zero
PECA_SEM_ESTOQUE_ID=$(curl -s -X POST http://localhost:8083/api/v1/pecas-insumos \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "Amortecedor Traseiro",
    "descricao": "Amortecedor sem estoque",
    "preco": 210.00,
    "quantidadeEmEstoque": 0,
    "unidadeDeMedida": "Unidade"
  }' | jq -r '.pecaInsumoId')

curl -s -X POST http://localhost:8080/api/v1/ordens-servico/completa \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "clienteId": "{CLIENTE_ID}",
    "veiculoId": "{VEICULO_ID}",
    "servicos": [
      { "servicoId": "{SERVICO_ID}", "quantidade": 1 }
    ],
    "pecas": [
      { "pecaInsumoId": "'"$PECA_SEM_ESTOQUE_ID"'", "quantidade": 1 }
    ]
  }' | jq .
```

**Resultado esperado:** `422 Unprocessable Entity`

```json
{
  "code": "OrdemServico.PecaIndisponivel",
  "description": "Peça/insumo indisponível para a quantidade informada."
}
```

> ✅ Nenhuma OS é criada e nenhum estoque é reservado — a verificação de disponibilidade
> via `IPecaDisponibilidadeGateway` ocorre antes de chamar `OrdemServico.AbrirComServicos`.
> Se o `pecaInsumoId` não existir de fato, o código retornado é
> `OrdemServico.PecaNaoEncontrada` em vez de `PecaIndisponivel`.

---

## A20 — Consultar Status de OS Inexistente

```bash
curl -s http://localhost:8080/api/v1/ordens-servico/00000000-0000-0000-0000-000000000099/status \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  | jq .
# Esperado: 404
```

> ✅ `GET /{id}/status` reaproveita a mesma query `ObterOrdemServicoPorIdQuery` do
> `GET /{id}`, então o comportamento de 404 para OS inexistente é idêntico.

---

## A21 — Pagamento Recusado (`OTHE`) e Compensação da Saga ⭐

> 🔑 **Token:** `$TOKEN_OFICINA` na abertura da OS e `$TOKEN_CLIENTE` para aprovar e pagar.

Valida a compensação por pagamento recusado: `PagamentoRecusado` → a OS rejeita o orçamento aprovado →
`OrcamentoRejeitado` → o PecasInsumos estorna as peças. Fluxo completo em
[`saga.md`](../arquitetura/diagramas/saga.md).

```bash
# 1) abrir a OS completa e anotar o estoque depois da baixa (assíncrona)
OS_REC_ID=$(curl -s -X POST http://localhost:8080/api/v1/ordens-servico/completa \
  -H "Authorization: Bearer $TOKEN_OFICINA" -H "Content-Type: application/json" \
  -d '{"clienteId": "{CLIENTE_ID}", "veiculoId": "{VEICULO_ID}",
       "servicos": [{ "servicoId": "{SERVICO_ID}", "quantidade": 1 }],
       "pecas": [{ "pecaInsumoId": "{PECA_ID}", "quantidade": 2 }]}' | jq -r '.id')
sleep 5
curl -s http://localhost:8083/api/v1/pecas-insumos/{PECA_ID} -H "Authorization: Bearer $TOKEN_OFICINA" | jq '.quantidadeEstoque'

# 2) aprovar o orçamento (cria o pagamento Pendente) e pagar com um titular recusado
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/$OS_REC_ID/aprovar-orcamento \
  -H "Authorization: Bearer $TOKEN_CLIENTE" > /dev/null
sleep 3
curl -s -X POST http://localhost:8084/api/v1/pagamentos/ordens-servico/$OS_REC_ID/pagar \
  -H "Authorization: Bearer $TOKEN_CLIENTE" -H "Content-Type: application/json" \
  -d '{"titularCartao": "OTHE"}' | jq '{status, motivoRecusa}'

# 3) conferir a compensação (aguarde alguns segundos pelos eventos)
sleep 5
curl -s http://localhost:8080/api/v1/ordens-servico/$OS_REC_ID -H "Authorization: Bearer $TOKEN_OFICINA" \
  | jq '{status, orcamentos: [.orcamentos[].status]}'
curl -s http://localhost:8083/api/v1/pecas-insumos/{PECA_ID} -H "Authorization: Bearer $TOKEN_OFICINA" | jq '.quantidadeEstoque'
```

**Resultado esperado:**
- o pagamento volta `200 OK` com `status: "Recusado"` e `motivoRecusa: "cc_rejected_other_reason"`;
- a OS fica `AguardandoAprovacao` com o orçamento `Rejeitado` (e apta a novo `registrar-diagnostico`);
- o estoque volta ao valor anterior à abertura (as 2 unidades foram estornadas);
- pagar de novo a mesma OS responde `404` (não há mais pagamento `Pendente`).

---

## Resumo de Respostas de Erro

| Situação | HTTP Status | Quando ocorre |
|---|---|---|
| Dado de entrada inválido (VO falha) | `422` | CPF/CNPJ/placa inválidos, campo obrigatório ausente |
| Regra de negócio violada | `422` | Transição inválida, estoque insuficiente, orçamento existente |
| Conflito de unicidade | `422` | Documento duplicado, placa duplicada |
| Recurso não encontrado | `404` | GET/DELETE de entidade inexistente |
| ACL: entidade cross-BC não existe | `422` | Cliente/veículo inexistente ao criar OS |
| Erro inesperado | `500` | Problem Details (RFC 7807) via middleware global |
