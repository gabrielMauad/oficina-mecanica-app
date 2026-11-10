namespace OrdensServico.Adapters.Gateways.Http;

// Recortes das respostas dos serviços Cadastro (GET /api/v1/clientes|veiculos|servicos/{id}) e
// PecasInsumos (GET /api/v1/pecas-insumos/{id} e /api/v1/pecas-insumos/{id}/disponibilidade):
// apenas os campos que a OS consome. Campos extras do JSON são ignorados na desserialização.

internal sealed record ClienteResposta(Guid Id, string Nome, string Email, bool Ativo);

internal sealed record VeiculoResposta(Guid Id, string Placa, Guid ClienteId);

internal sealed record ServicoResposta(Guid Id, string Nome, decimal Preco);

internal sealed record PecaInsumoResposta(Guid Id, string Nome, string UnidadeDeMedida);

internal sealed record DisponibilidadeResposta(bool Disponivel, decimal PrecoUnitario);
