# RFC-001 — Estratégia de autenticação

- **Status:** Aprovado
- **Data:** 2026-09-07
- **Decisões derivadas:** [ADR-001](../adrs/001-jwt-hs256-segredo-compartilhado.md),
  [ADR-002](../adrs/002-lambda-le-o-banco-diretamente.md),
  [ADR-003](../adrs/003-dois-emissores-e-autorizacao-por-papel.md)
- **Atualização da Fase 4:** os dois emissores continuam, mas o da oficina é o serviço `autenticacao` e a
  Function deixou de ler o banco — consulta o Cadastro por HTTP
  ([ADR-011](../adrs/011-lambda-consulta-o-cadastro-por-http.md), que substitui a ADR-002). As seções
  abaixo que falam em leitura direta de `cadastro.cliente` descrevem o desenho da Fase 3.

---

## 1. Contexto e problema

Até a Fase 2 a autenticação era um detalhe interno da aplicação: a rota `POST /api/v1/auth/login`
comparava email e senha com um usuário administrador vindo de configuração e emitia um JWT assinado
e validado pelo mesmo processo. Existia um único ator autenticado e um único nível de acesso — toda
rota `[Authorize]` era acessível por qualquer token válido.

A Fase 3 muda três coisas ao mesmo tempo:

1. **Um novo ator**: o cliente da oficina, que se autentica pelo **CPF**.
2. **Um novo emissor**: uma **Function Serverless** passa a validar o CPF, consultar a existência e o
   status do cliente na base e emitir o token.
3. **Uma nova borda**: um **API Gateway** passa a rotear e controlar o acesso às rotas protegidas.

Isso levanta quatro perguntas que precisam ser respondidas antes de escrever código, porque cada uma
condiciona o que será construído na aplicação e na Lambda.

---

## 2. Questão 1 — Como o token é assinado

Emissor e validador deixam de ser o mesmo processo, então a chave precisa atravessar uma fronteira.

| | HS256 (simétrico) | RS256 (assimétrico + JWKS) |
|---|---|---|
| Chave | Um segredo compartilhado | Par privada/pública |
| Mudança na aplicação | Mínima — já é `SymmetricSecurityKey` | Substituir por busca e cache de JWKS |
| Infraestrutura adicional | Nenhuma | Dois endpoints `/.well-known/` públicos |
| JWT authorizer nativo do API Gateway | **Não funciona** (exige emissor OIDC) | Funciona |
| Quem valida pode forjar? | Sim | Não |

**Decisão: HS256.** A aplicação já está construída assim e a conta AWS disponível é uma AWS Academy
ainda não explorada — vale reduzir o número de elementos que dependem dela. A perda concreta é o
authorizer nativo do gateway; a validação fica na aplicação, e um *Lambda authorizer* próprio é a
alternativa caso se queira validar também na borda. O gateway continua responsável por roteamento e
controle, que é o que o enunciado exige dele.

Detalhes e consequências em [ADR-001](../adrs/001-jwt-hs256-segredo-compartilhado.md).

---

## 3. Questão 2 — Como a Lambda consulta o cliente

O dado necessário (existe um cliente com esse CPF? está ativo?) vive em `cadastro.cliente`, tabela
do bounded context Cadastro, cujo schema é evoluído pelas migrations da aplicação.

**Alternativa considerada — a Lambda chama um endpoint interno da aplicação.** Manteria o schema com
um dono só, mas cria dependência circular (o autenticador passando a depender do serviço que
protege), derruba o login quando a aplicação está indisponível, exige expor uma rota sem
autenticação e custa mais trabalho: endpoint novo **e** cliente HTTP.

**Alternativa considerada — a Lambda repassa a chamada para a rota de login da aplicação.** É o menor
esforço possível e reaproveitaria todo o `LoginHandler`. Descartada por aderência ao requisito: a
Function ficaria sem executar nenhuma das três responsabilidades que a spec atribui a ela, sendo um
repasse removível sem alterar o comportamento do sistema.

**Decisão: leitura direta do banco**, com usuário dedicado somente-leitura e um teste de integração
da Lambda contra as migrations da aplicação, para que uma mudança de schema quebre o build em vez do
login. É uma exceção deliberada ao isolamento entre contextos, restrita ao fluxo de autenticação.

Detalhes em [ADR-002](../adrs/002-lambda-le-o-banco-diretamente.md).

---

## 4. Questão 3 — Quem são os atores e o que cada um acessa

As rotas protegidas do sistema são, em sua maioria, **operações da oficina**. O cliente participa de
dois momentos: acompanhar a OS e decidir sobre o orçamento. Se a autenticação por CPF simplesmente
substituísse a existente, qualquer cliente ativo teria acesso ao estoque e ao cadastro de terceiros.

**Decisão: dois fluxos coexistem, com autorização por papel.** A justificativa formal para não
autenticar tudo por CPF é que **funcionário não é cliente e não deve ser autenticado por um
identificador público** — CPF não é segredo. O requisito da spec é atendido: as rotas do cliente são
protegidas por autenticação via CPF.

Detalhes em [ADR-003](../adrs/003-dois-emissores-e-autorizacao-por-papel.md).

### 4.1 Contrato do token

Ambos os tokens são assinados com o mesmo segredo e distinguidos por `iss` e pela claim de papel.

| Claim | Token do cliente | Token da oficina |
|---|---|---|
| `iss` | `oficina-mecanica-auth` (Lambda) | `oficina-mecanica-app` |
| `aud` | `oficina-mecanica-api` | `oficina-mecanica-api` |
| `sub` | Id do cliente (GUID) | Email do usuário |
| `role` | `Cliente` | `Oficina` |
| `cpf` | CPF somente dígitos | — |
| `exp` | 1 hora | 1 hora |

A aplicação passa a validar `issuer` e `audience` — hoje ambos estão desligados no `Program.cs` —
aceitando os dois emissores acima e a audience única.

### 4.2 Mapa de rotas por papel

| Rota | Papel exigido |
|---|---|
| `POST /api/v1/auth/login` | Anônima |
| `GET /healthz` | Anônima |
| `GET /api/v1/ordens-servico/acompanhamento` | `Cliente` |
| `PATCH /api/v1/ordens-servico/{id}/aprovar-orcamento` | `Cliente` |
| `PATCH /api/v1/ordens-servico/{id}/rejeitar-orcamento` | `Cliente` |
| `GET /api/v1/ordens-servico/{id}` e `/{id}/status` | `Cliente` ou `Oficina` |
| Demais rotas de `ordens-servico` (abertura, diagnóstico, execução, finalização, conclusão) | `Oficina` |
| Todas as rotas de `clientes`, `veiculos`, `servicos` e `pecas-insumos` | `Oficina` |

Regra adicional a implementar junto com o mapa: um token de papel `Cliente` só pode operar sobre
ordens de serviço **do próprio cliente**, comparando o `sub` do token com o dono da OS. Sem isso, a
separação de papéis protege o estoque mas não protege um cliente do outro.

---

## 5. Questão 4 — Onde o token é validado

Com HS256, o JWT authorizer nativo do API Gateway está fora. Restam duas posições:

- **Na aplicação** (decisão atual): já é onde a validação existe hoje; custo zero.
- **Também na borda**, com um *Lambda authorizer* próprio: rejeita tráfego inválido antes de chegar
  ao cluster e é um argumento melhor de arquitetura. Fica como melhoria opcional da fase de
  infraestrutura, sem bloquear nada.

---

## 6. Impacto no trabalho

**Na aplicação:** validar `issuer`/`audience`; adicionar claim de papel ao token da oficina;
aplicar autorização por papel rota a rota; garantir que cliente só acesse a própria OS; ajustar os
testes de integração, que hoje autenticam tudo com o usuário administrador.

**Na Lambda:** validar o CPF (formato e dígitos verificadores), consultar `cadastro.cliente`, assinar
o token conforme o contrato acima, e um teste de integração contra as migrations da aplicação.

**Fora do escopo desta fase:** rotação automática do segredo, refresh token, e cadastro de múltiplos
usuários da oficina — o usuário administrador continua vindo de configuração.

---

## 7. Questões em aberto

- ~~`GET /api/v1/ordens-servico` está hoje anônima~~ **Resolvida.** Passou a exigir papel
  `Oficina`. A listagem por cliente (`?clienteId=`) devolve dados de qualquer cliente da base —
  mesmo sendo filtrável por id, é uma consulta de estoque de informação (quais OS um cliente tem,
  seus status e valores) que não deveria ficar exposta sem autenticação, e o requisito de
  "comportamento público" da Fase 2 não sobrevive à introdução de papéis nesta fase: não há mais
  um único nível de acesso a preservar. Em contrapartida, o acompanhamento do próprio cliente
  continua disponível — pelo token de CPF, em `GET /api/v1/ordens-servico/acompanhamento`
  (seção 4.2), agora também filtrado pelo `sub` do token para não expor OS de terceiros.
- **Validação na borda** (seção 5) fica pendente de tempo e de acesso à infraestrutura.

---

## 8. Superfície não autenticada da documentação OpenAPI

Decisão tomada durante a implementação da fase, registrada aqui por afetar diretamente a
superfície pública da aplicação.

A documentação OpenAPI/Scalar deixou de ser exposta apenas em `Development` e passa a ser exposta
**em qualquer ambiente por padrão**, controlada pela flag de configuração `OpenApi:Enabled`
(variável `OpenApi__Enabled`), cujo default é `true`.

**Consequência explícita:** a documentação completa da API — rotas, contratos de request e
response, e o mapa de quais rotas exigem autenticação — fica **acessível sem autenticação em
qualquer ambiente publicado**. Nenhum dado de negócio é exposto por ela; o que é exposto é a
descrição da superfície da API.

**Por que foi aceito:** o requisito de entrega da fase pede o link do Swagger da API publicada no
README, o que só é atendível com a documentação acessível fora de `Development`. A alternativa
(protegê-la com autenticação) tornaria o link inútil como evidência de entrega.

**Mitigação:** a exposição é **configuração, não código** — basta `OpenApi__Enabled=false` no
ConfigMap ou nas variáveis de ambiente para desligá-la, sem recompilar nem republicar a imagem.
Os testes de integração já rodam com a flag desligada.

Fica em aberto, para uma fase de endurecimento: restringir a documentação por rede (acesso apenas
de dentro da VPC ou via API Gateway autenticado) em vez de por flag.
