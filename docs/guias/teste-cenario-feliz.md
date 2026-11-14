# Guia de Testes E2E — Cenário Feliz (Happy Path)

> **Pré-requisito:** os cinco serviços e a infraestrutura compartilhada no ar.
> Scalar disponível em `http://localhost:<porta>/scalar` de cada serviço.
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
> **Como usar este guia:**
> Cada passo retorna um ID nos campos indicados. Copie-o e substitua nos passos
> subsequentes onde indicado com `{VARIAVEL}`.
>
> **Todas as chamadas exigem token**, e o *papel* do token importa — cada passo indica qual
> usar. Comece pelo **Passo 0**.

---

## Passo 0 — Obter os tokens

A autorização é por papel (ver [RFC-001 §4.2](../arquitetura/rfcs/001-estrategia-de-autenticacao.md)).
Existem dois tokens, e usar o errado devolve **403 Forbidden**:

| Variável | Papel | Usada em |
|---|---|---|---|
| `$TOKEN_OFICINA` | `Oficina` | tudo, exceto o que está abaixo |
| `$TOKEN_CLIENTE` | `Cliente` | aprovar/rejeitar orçamento, pagar e `/acompanhamento` |

### Token da oficina (papel `Oficina`)

Emitido pelo serviço **autenticacao** (porta `8081`) — é a **única rota anônima** além dos health checks:

```bash
export TOKEN_OFICINA=$(curl -s -X POST http://localhost:8081/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email": "admin@oficina.com", "senha": "admin123"}' | jq -r .token)

echo "$TOKEN_OFICINA"
```

### Token do cliente (papel `Cliente`)

> ⚠️ **Nenhum dos cinco serviços emite o token de cliente.** Na nuvem ele vem da **Function
> Serverless** que autentica por CPF (`POST <endpoint>/auth/cpf`, repositório
> `oficina-mecanica-lambda-auth`). Em ambiente local a Function não roda, então o token é obtido
> **manualmente** (abaixo) ou pelo helper dos testes de integração
> (`OficinaMecanicaWebApplicationFactory.CreateClienteAuthenticatedClient`).

Para gerar um manualmente, assine um JWT **HS256** com o mesmo `Jwt__Secret` da aplicação
(`docker-dev-secret-key-replace-in-prod-000` nos `docker-compose.yml`) e com estas claims:

| Claim | Valor |
|---|---|
| `iss` | `oficina-mecanica-auth` |
| `aud` | `oficina-mecanica-api` |
| `sub` | o `{CLIENTE_ID}` retornado no Passo 1 |
| `role` | `Cliente` |
| `exp` | 1 hora |

Qualquer ferramenta de JWT serve (por exemplo, colar as claims e o segredo em
[jwt.io](https://jwt.io)). Depois:

```bash
export TOKEN_CLIENTE="<cole aqui o JWT gerado>"
```

> O `sub` **precisa ser o id do cliente dono da OS**: a aplicação compara o `sub` com o dono e
> devolve **403** se um cliente tentar acessar a OS de outro. Como o `{CLIENTE_ID}` só existe
> depois do Passo 1, gere este token entre o Passo 1 e o Passo 10.

---

## Visão geral do fluxo

```
[1] Criar Cliente
[2] Cadastrar Veículo (vinculado ao cliente)
[3] Adicionar Serviço ao catálogo
[4] Adicionar Peça/Insumo ao estoque
[5] Incrementar estoque da peça
[6] Criar Ordem de Serviço (OS)
[7] Iniciar Diagnóstico
[8] Registrar Diagnóstico  ← dispara automaticamente:
                               - Geração e envio do orçamento (OS → AguardandoAprovacao)
                               - Decremento de estoque (integration event, assíncrono)
[9] Verificar estoque decrementado
[10] Aprovar Orçamento
[11] Pagar o orçamento (serviço Pagamento, titular APRO) ← dispara:
                               - PagamentoAprovado → OS passa a EmExecucao (evento)
[12] Finalizar OS           ← dispara automaticamente:
                               - Notificação ao cliente (notificado_em preenchido)
[13] Concluir OS
[14] Verificar estado final
[15] Consultar OS por Cliente
[16] Consultar Status Resumido da OS
[17] Listar OS para Acompanhamento (token do cliente — filtra pelo cliente do token)
```

> ℹ️ Existe também um fluxo alternativo que colapsa os passos [6]–[8] em uma única
> chamada: `POST /ordens-servico/completa`. Veja a seção "Fluxo Alternativo — Abrir OS
> Já Completa" mais abaixo.

---

## Passo 1 — Criar Cliente

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

```bash
curl -s -X POST http://localhost:8082/api/v1/clientes \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "João Silva",
    "documento": "529.982.247-25",
    "email": "joao.silva@email.com",
    "telefone": "(31) 99999-0001",
    "pessoaFisica": true
  }' | jq .
```

**Resultado esperado:** `201 Created`

```json
{
  "clienteId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "nome": "João Silva",
  "documento": "52998224725",
  "email": "joao.silva@email.com",
  "telefone": "(31) 99999-0001",
  "ativo": true
}
```

> ⚙️ **Salve:** `CLIENTE_ID = clienteId`

---

## Passo 2 — Cadastrar Veículo

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

```bash
curl -s -X POST http://localhost:8082/api/v1/veiculos \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "placa": "ABC1D23",
    "modelo": "Onix",
    "marca": "Chevrolet",
    "ano": 2023,
    "clienteId": "{CLIENTE_ID}"
  }' | jq .
```

**Resultado esperado:** `201 Created`

```json
{
  "veiculoId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "placa": "ABC1D23",
  "modelo": "Onix",
  "marca": "Chevrolet",
  "ano": 2023,
  "clienteId": "{CLIENTE_ID}"
}
```

> ⚙️ **Salve:** `VEICULO_ID = veiculoId`

---

## Passo 3 — Adicionar Serviço ao Catálogo

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

```bash
curl -s -X POST http://localhost:8082/api/v1/servicos \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "Troca de Óleo",
    "descricao": "Troca de óleo do motor com filtro incluso",
    "preco": 150.00
  }' | jq .
```

**Resultado esperado:** `201 Created`

```json
{
  "servicoId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "nome": "Troca de Óleo",
  "descricao": "Troca de óleo do motor com filtro incluso",
  "precoBase": 150.00,
  "ativo": true
}
```

> ⚙️ **Salve:** `SERVICO_ID = servicoId`

---

## Passo 4 — Adicionar Peça/Insumo ao Estoque

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

```bash
curl -s -X POST http://localhost:8083/api/v1/pecas-insumos \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "Filtro de Óleo",
    "descricao": "Filtro de óleo para motores flex",
    "preco": 45.90,
    "quantidadeEmEstoque": 0,
    "unidadeDeMedida": "Unidade"
  }' | jq .
```

**Resultado esperado:** `201 Created`

```json
{
  "pecaInsumoId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "nome": "Filtro de Óleo",
  "descricao": "Filtro de óleo para motores flex",
  "precoUnitario": 45.90,
  "quantidadeEstoque": 0,
  "unidadeMedida": "Unidade",
  "ativo": true
}
```

> ⚙️ **Salve:** `PECA_ID = pecaInsumoId`

---

## Passo 5 — Incrementar Estoque da Peça

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

Adicionamos 10 unidades ao estoque antes de usar na OS.

```bash
curl -s -X PATCH http://localhost:8083/api/v1/pecas-insumos/{PECA_ID}/estoque/entrada \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "quantidade": 10
  }' | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "pecaInsumoId": "{PECA_ID}",
  "quantidadeEstoque": 10
}
```

> ✅ Estoque agora tem **10 unidades**. A OS vai consumir 2.

---

## Passo 6 — Criar Ordem de Serviço

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

```bash
curl -s -X POST http://localhost:8080/api/v1/ordens-servico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "clienteId": "{CLIENTE_ID}",
    "veiculoId": "{VEICULO_ID}"
  }' | jq .
```

**Resultado esperado:** `201 Created`

```json
{
  "id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "clienteId": "{CLIENTE_ID}",
  "veiculoId": "{VEICULO_ID}",
  "status": "Recebida",
  "descricaoDiagnostico": null,
  "notificadoEm": null,
  "entregueEm": null,
  "itensServico": [],
  "itensPeca": [],
  "orcamentos": []
}
```

> ⚙️ **Salve:** `OS_ID = id`

---

## Passo 7 — Iniciar Diagnóstico

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

```bash
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/{OS_ID}/iniciar-diagnostico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "id": "{OS_ID}",
  "status": "EmDiagnostico",
  ...
}
```

> ✅ OS transitou de `Recebida` → `EmDiagnostico`.

---

## Passo 8 — Registrar Diagnóstico

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

Esta é a chamada mais importante do fluxo. Ao retornar com sucesso, **três coisas acontecem automaticamente**:
1. O orçamento é criado com `Status = Pendente`
2. O orçamento é enviado ao cliente (`Status = Enviado`), e a OS transita para `AguardandoAprovacao`
3. O integration event `OrcamentoGeradoIntegrationEvent` é publicado, e o módulo PecasInsumos decrementa o estoque

```bash
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/{OS_ID}/registrar-diagnostico \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "descricaoDiagnostico": "Motor com desgaste acentuado. Necessário troca de óleo e filtro.",
    "servicos": [
      {
        "servicoId": "{SERVICO_ID}",
        "quantidade": 1
      }
    ],
    "pecas": [
      {
        "pecaInsumoId": "{PECA_ID}",
        "quantidade": 2
      }
    ]
  }' | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "id": "{OS_ID}",
  "status": "EmDiagnostico",
  "descricaoDiagnostico": "Motor com desgaste acentuado. Necessário troca de óleo e filtro.",
  "itensServico": [
    {
      "servicoId": "{SERVICO_ID}",
      "quantidade": 1,
      "precoUnitarioSnapshot": 150.00
    }
  ],
  "itensPeca": [
    {
      "pecaInsumoId": "{PECA_ID}",
      "quantidade": 2,
      "precoUnitarioSnapshot": 45.90
    }
  ],
  "orcamentos": [
    {
      "valorTotal": 241.80,
      "status": "Pendente",
      "dataGeracao": "...",
      "dataEnvio": null,
      "dataAprovacao": null
    }
  ]
}
```

> ⚠️ A resposta ainda mostra `status: "EmDiagnostico"` e orçamento `"Pendente"` porque
> reflete o estado **antes** dos domain event handlers rodarem. Os handlers são chamados
> **após** o SaveChanges. Consulte a OS em seguida para ver o estado atualizado.

---

## Passo 8a — Verificar OS após os handlers automáticos

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`). Aceita também `$TOKEN_CLIENTE`,
> mas nesse caso **só a própria OS** — o token de outro cliente recebe **403**.

```bash
curl -s http://localhost:8080/api/v1/ordens-servico/{OS_ID} \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "id": "{OS_ID}",
  "status": "AguardandoAprovacao",
  "descricaoDiagnostico": "Motor com desgaste acentuado. Necessário troca de óleo e filtro.",
  "orcamentos": [
    {
      "valorTotal": 241.80,
      "status": "Enviado",
      "dataGeracao": "...",
      "dataEnvio": "...",
      "dataAprovacao": null
    }
  ]
}
```

> ✅ OS está em `AguardandoAprovacao` e orçamento em `Enviado`.
> Valor total: `1 × R$ 150,00 (serviço) + 2 × R$ 45,90 (peça) = R$ 241,80`

---

## Passo 9 — Verificar Estoque Decrementado

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

```bash
curl -s http://localhost:8083/api/v1/pecas-insumos/{PECA_ID} \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "pecaInsumoId": "{PECA_ID}",
  "nome": "Filtro de Óleo",
  "quantidadeEstoque": 8
}
```

> ✅ Estoque foi de **10 → 8** (2 unidades consumidas via integration event).
> Isso confirma que o `OrcamentoGeradoIntegrationEvent` foi publicado e consumido
> pelo handler em `PecasInsumos.Application`.

---

## Passo 10 — Aprovar Orçamento

> 🔑 **Token:** `$TOKEN_CLIENTE` (papel `Cliente`). Com o token da oficina esta rota devolve **403**.
> O `sub` do token precisa ser o `{CLIENTE_ID}` **dono desta OS** — outro cliente também recebe **403**.

```bash
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/{OS_ID}/aprovar-orcamento \
  -H "Authorization: Bearer $TOKEN_CLIENTE" \
  | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "id": "{OS_ID}",
  "status": "AguardandoAprovacao",
  "orcamentos": [
    {
      "valorTotal": 241.80,
      "status": "Aprovado",
      "dataAprovacao": "..."
    }
  ]
}
```

> ✅ Orçamento aprovado. `dataAprovacao` preenchida.

---

## Passo 11 — Pagar o orçamento (OS passa a execução)

> 🔑 **Token:** `$TOKEN_CLIENTE` (papel `Cliente`, `sub` = `{CLIENTE_ID}`, o dono da OS).

Ao aprovar o orçamento (Passo 10), o serviço **Pagamento** cria um pagamento `Pendente`. Confira e
pague com o titular `APRO` (o mock do Mercado Pago aprova; qualquer outro titular, como `OTHE`,
recusa — ver o cenário A21 do guia de cenários alternativos):

```bash
# o pagamento é criado de forma assíncrona (evento OrcamentoAprovado): pode levar alguns segundos
curl -s http://localhost:8084/api/v1/pagamentos/ordens-servico/{OS_ID} \
  -H "Authorization: Bearer $TOKEN_CLIENTE" | jq .

curl -s -X POST http://localhost:8084/api/v1/pagamentos/ordens-servico/{OS_ID}/pagar \
  -H "Authorization: Bearer $TOKEN_CLIENTE" \
  -H "Content-Type: application/json" \
  -d '{"titularCartao": "APRO"}' | jq .
```

**Resultado esperado:** `200 OK` com o pagamento `Aprovado` (`valor` = 241,80). A OS passa a
`EmExecucao` quando o evento `PagamentoAprovado` chega — aguarde alguns segundos e consulte:

```bash
curl -s http://localhost:8080/api/v1/ordens-servico/{OS_ID} \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq '{id, status}'
```

```json
{
  "id": "{OS_ID}",
  "status": "EmExecucao"
}
```

> ✅ OS em `EmExecucao`: o mecânico está trabalhando. Não há rota para executar manualmente.

---

## Passo 12 — Finalizar OS

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

Ao finalizar, o domain event `OrdemServicoFinalizada` é emitido. O handler
`NotificarClienteAoFinalizar` reage e preenche `notificado_em` automaticamente.

```bash
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/{OS_ID}/finalizar \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "id": "{OS_ID}",
  "status": "Finalizada",
  "notificadoEm": null
}
```

> ⚠️ `notificadoEm` pode aparecer `null` na resposta imediata (mesmo motivo do passo 8:
> o handler roda após o SaveChanges). Consulte a OS para confirmar.

---

## Passo 12a — Verificar notificação automática

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

```bash
curl -s http://localhost:8080/api/v1/ordens-servico/{OS_ID} \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "id": "{OS_ID}",
  "status": "Finalizada",
  "notificadoEm": "2026-05-26T15:30:00Z"
}
```

> ✅ `notificadoEm` preenchido automaticamente pelo handler. Nos logs da aplicação
> deve aparecer a mensagem stub de notificação ao cliente.

---

## Passo 13 — Concluir OS (Entrega do Veículo)

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

```bash
curl -s -X PATCH http://localhost:8080/api/v1/ordens-servico/{OS_ID}/concluir \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "id": "{OS_ID}",
  "status": "Entregue",
  "entregueEm": "2026-05-26T15:35:00Z"
}
```

> ✅ OS finalizada com sucesso! Status: `Entregue`.

---

## Passo 14 — Verificar Estado Final Completo

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

```bash
curl -s http://localhost:8080/api/v1/ordens-servico/{OS_ID} \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
```

**Resultado esperado:** `200 OK` — snapshot completo da OS:

```json
{
  "id": "{OS_ID}",
  "clienteId": "{CLIENTE_ID}",
  "veiculoId": "{VEICULO_ID}",
  "status": "Entregue",
  "descricaoDiagnostico": "Motor com desgaste acentuado. Necessário troca de óleo e filtro.",
  "notificadoEm": "2026-05-26T15:30:00Z",
  "entregueEm": "2026-05-26T15:35:00Z",
  "itensServico": [
    {
      "servicoId": "{SERVICO_ID}",
      "quantidade": 1,
      "precoUnitarioSnapshot": 150.00
    }
  ],
  "itensPeca": [
    {
      "pecaInsumoId": "{PECA_ID}",
      "quantidade": 2,
      "precoUnitarioSnapshot": 45.90
    }
  ],
  "orcamentos": [
    {
      "valorTotal": 241.80,
      "status": "Aprovado",
      "dataGeracao": "...",
      "dataEnvio": "...",
      "dataAprovacao": "..."
    }
  ]
}
```

---

## Passo 15 — Consultar OS por Cliente

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`). Esta rota **deixou de ser anônima**
> na Fase 3 — uma listagem completa de OS é informação sensível.

```bash
curl -s "http://localhost:8080/api/v1/ordens-servico?clienteId={CLIENTE_ID}" \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
```

**Resultado esperado:** `200 OK` — lista com a OS criada.

---

## Passo 16 — Consultar Status Resumido da OS

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`). Aceita também `$TOKEN_CLIENTE`,
> mas nesse caso **só a própria OS** — o token de outro cliente recebe **403**.

Endpoint leve, retorna apenas `id` e `status` — útil para polling frequente sem trazer o
snapshot completo da OS.

```bash
curl -s http://localhost:8080/api/v1/ordens-servico/{OS_ID}/status \
  -H "Authorization: Bearer $TOKEN_OFICINA" | jq .
```

**Resultado esperado:** `200 OK`

```json
{
  "id": "{OS_ID}",
  "status": "Entregue"
}
```

---

## Passo 17 — Listar OS para Acompanhamento (do cliente do token)

> 🔑 **Token:** `$TOKEN_CLIENTE` (papel `Cliente`). Com o token da oficina esta rota devolve **403**.

Lista as OS "ativas" (exclui `Finalizada` e `Entregue`), ordenadas por prioridade de
status — `EmExecucao` → `AguardandoAprovacao` → `EmDiagnostico` → `Recebida` — e, dentro de
cada status, as mais antigas primeiro. A lista é **filtrada pelo cliente do token** (`sub`),
então só aparecem as OS desse cliente.

```bash
curl -s http://localhost:8080/api/v1/ordens-servico/acompanhamento \
  -H "Authorization: Bearer $TOKEN_CLIENTE" | jq .
```

**Resultado esperado:** `200 OK` — lista de OS ativas. Como a OS deste guia já está
`Entregue`, ela **não** deve aparecer nesta lista.

---

## Fluxo Alternativo — Abrir OS Já Completa (`POST /completa`)

> 🔑 **Token:** `$TOKEN_OFICINA` (papel `Oficina`).

Endpoint novo que abre a OS diretamente com serviços e peças definidos, pulando as
etapas manuais de "iniciar diagnóstico" + "registrar diagnóstico" (Passos 7–8). A OS já
nasce em `AguardandoAprovacao`, com orçamento `Enviado` e estoque reservado — mesmos
efeitos colaterais do Passo 8, em uma única chamada.

```bash
curl -s -X POST http://localhost:8080/api/v1/ordens-servico/completa \
  -H "Authorization: Bearer $TOKEN_OFICINA" \
  -H "Content-Type: application/json" \
  -d '{
    "clienteId": "{CLIENTE_ID}",
    "veiculoId": "{VEICULO_ID}",
    "servicos": [
      {
        "servicoId": "{SERVICO_ID}",
        "quantidade": 1
      }
    ],
    "pecas": [
      {
        "pecaInsumoId": "{PECA_ID}",
        "quantidade": 2
      }
    ]
  }' | jq .
```

**Resultado esperado:** `201 Created`

```json
{
  "id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "clienteId": "{CLIENTE_ID}",
  "veiculoId": "{VEICULO_ID}",
  "status": "AguardandoAprovacao",
  "descricaoDiagnostico": null,
  "itensServico": [
    {
      "servicoId": "{SERVICO_ID}",
      "quantidade": 1,
      "precoUnitarioSnapshot": 150.00
    }
  ],
  "itensPeca": [
    {
      "pecaInsumoId": "{PECA_ID}",
      "quantidade": 2,
      "precoUnitarioSnapshot": 45.90
    }
  ],
  "orcamentos": [
    {
      "valorTotal": 241.80,
      "status": "Enviado",
      "dataGeracao": "...",
      "dataEnvio": "...",
      "dataAprovacao": null
    }
  ]
}
```

> ✅ Diferente do fluxo manual, aqui a OS já nasce em `AguardandoAprovacao` com orçamento
> `Enviado` e `descricaoDiagnostico: null` — não existe etapa de diagnóstico neste fluxo.
> A partir daqui, a OS segue o mesmo ciclo de vida do fluxo principal:
> `aprovar-orcamento → pagar (evento PagamentoAprovado → EmExecucao) → finalizar → concluir` (Passos 10–13).

> ⚙️ **Salve:** `OS_COMPLETA_ID = id`

---

## Checklist de Validação

| # | Verificação | Token | Esperado |
|---|---|---|---|
| 1 | POST /clientes com CPF válido | `Oficina` | 201, clienteId retornado |
| 2 | POST /veiculos com placa Mercosul | `Oficina` | 201, veiculoId retornado |
| 3 | POST /servicos | `Oficina` | 201, servicoId retornado |
| 4 | POST /pecas-insumos | `Oficina` | 201, pecaInsumoId retornado |
| 5 | PATCH estoque/entrada | `Oficina` | 200, estoque = 10 |
| 6 | POST /ordens-servico | `Oficina` | 201, status = Recebida |
| 7 | PATCH iniciar-diagnostico | `Oficina` | 200, status = EmDiagnostico |
| 8 | PATCH registrar-diagnostico | `Oficina` | 200, orçamento criado |
| 8a | GET /ordens-servico/{id} | `Oficina` | status = AguardandoAprovacao, orçamento = Enviado |
| 9 | GET /pecas-insumos/{id} | `Oficina` | estoque = 8 (decrementou 2) |
| 10 | PATCH aprovar-orcamento | `Cliente` | 200, orçamento = Aprovado |
| 11 | POST /pagamentos/ordens-servico/{id}/pagar (`APRO`) | `Cliente` | 200, pagamento Aprovado; a OS vira EmExecucao pelo evento |
| 12 | PATCH finalizar | `Oficina` | 200, status = Finalizada |
| 12a | GET /ordens-servico/{id} | `Oficina` | notificadoEm preenchido |
| 13 | PATCH concluir | `Oficina` | 200, status = Entregue, entregueEm preenchido |
| 14 | GET /ordens-servico/{id} final | `Oficina` | Snapshot completo correto |
| 15 | GET /ordens-servico?clienteId=... | `Oficina` | Lista a OS criada |
| 16 | GET /ordens-servico/{id}/status | `Oficina` | 200, retorna apenas {id, status} |
| 17 | GET /ordens-servico/acompanhamento | `Cliente` | 200, lista OS ativas ordenadas por prioridade, sem Entregue |
| 18 | POST /ordens-servico/completa | `Oficina` | 201, status = AguardandoAprovacao direto, orçamento = Enviado, sem diagnóstico |
