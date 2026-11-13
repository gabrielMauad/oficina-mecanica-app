# language: pt
Funcionalidade: Fluxo completo da ordem de serviço e compensações da Saga
  Como oficina mecânica
  Quero que a ordem de serviço percorra o fluxo de orçamento, pagamento, execução e entrega
  E que as falhas de pagamento e de estoque desfaçam o que for preciso
  Para que o cliente só tenha o serviço executado depois de pagar e o estoque nunca fique inconsistente

  Cadastro e PecasInsumos (chamadas HTTP) são simulados por gateways falsos. Pagamento e
  PecasInsumos (mensageria) são simulados publicando e inspecionando mensagens no barramento
  da OrdensServico.

  Contexto:
    Dado um cliente com veículo, um serviço de 100 reais e uma peça de 40 reais com estoque suficiente
    E a oficina autenticada

  Cenário: Fluxo completo com pagamento aprovado até a entrega do veículo
    Quando a oficina abre a ordem de serviço completa com 1 serviço e 2 unidades da peça
    Então a ordem de serviço está "AguardandoAprovacao"
    E o orçamento está "Enviado"
    E o evento OrcamentoGerado foi publicado com 2 unidades da peça

    Quando o cliente aprova o orçamento
    Então o evento OrcamentoAprovado foi publicado para o cliente com valor total de 180 reais
    E o orçamento está "Aprovado"
    E a ordem de serviço está "AguardandoAprovacao"

    Quando chega o evento PagamentoAprovado do orçamento
    Então a ordem de serviço está "EmExecucao"

    Quando a oficina finaliza a ordem de serviço
    Então a ordem de serviço está "Finalizada"
    E o cliente foi notificado

    Quando a oficina conclui a ordem de serviço
    Então a ordem de serviço está "Entregue"

  Cenário: Compensação com pagamento recusado devolve as peças e permite novo diagnóstico
    Dado que a oficina abriu a ordem de serviço completa com 1 serviço e 3 unidades da peça
    E que o cliente aprovou o orçamento

    Quando chega o evento PagamentoRecusado do orçamento com o motivo "cc_rejected_other_reason"
    Então o orçamento está "Rejeitado"
    E o evento OrcamentoRejeitado foi publicado com 3 unidades da peça
    E a ordem de serviço está "AguardandoAprovacao"

    Quando a oficina registra um novo diagnóstico com 1 serviço e 1 unidade da peça
    Então o diagnóstico é aceito
    E a ordem de serviço possui 2 orçamentos

  Cenário: Compensação com falha na baixa de estoque rejeita o orçamento sem estorno
    Dado que a oficina abriu a ordem de serviço completa com 1 serviço e 2 unidades da peça

    Quando chega o evento EstoqueBaixaFalhou do orçamento com o motivo "estoque insuficiente"
    Então o orçamento está "Rejeitado"
    E nenhum evento OrcamentoRejeitado foi publicado
    E a ordem de serviço está "AguardandoAprovacao"

  Cenário: Pagamento aprovado de um orçamento rejeitado não coloca a ordem de serviço em execução
    Dado que a oficina abriu a ordem de serviço completa com 1 serviço e 2 unidades da peça
    E que o cliente aprovou o orçamento
    E que o pagamento do orçamento foi recusado
    E que a oficina registrou um novo diagnóstico com 1 serviço e 1 unidade da peça
    E que o cliente aprovou o novo orçamento

    Quando chega o evento PagamentoAprovado do primeiro orçamento, já rejeitado
    Então o evento foi consumido sem erro
    E a ordem de serviço está "AguardandoAprovacao"

  Cenário: Rejeição pelo cliente publica OrcamentoRejeitado para o estorno das peças
    Dado que a oficina abriu a ordem de serviço completa com 1 serviço e 4 unidades da peça

    Quando o cliente rejeita o orçamento
    Então o orçamento está "Rejeitado"
    E o evento OrcamentoRejeitado foi publicado com 4 unidades da peça
