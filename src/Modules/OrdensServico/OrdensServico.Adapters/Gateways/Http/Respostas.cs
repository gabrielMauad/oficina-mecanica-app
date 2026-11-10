namespace OrdensServico.Adapters.Gateways.Http;

// Recortes das respostas dos serviços Cadastro e PecasInsumos (ver 00-contratos.md §4.3/§4.4):
// apenas os campos que a OS consome. Campos extras do JSON são ignorados na desserialização.

internal sealed record ClienteResposta(Guid Id, string Nome, string Email, bool Ativo);

internal sealed record VeiculoResposta(Guid Id, string Placa, Guid ClienteId);

internal sealed record ServicoResposta(Guid Id, string Nome, decimal Preco);

internal sealed record PecaInsumoResposta(Guid Id, string Nome, string UnidadeDeMedida);

internal sealed record DisponibilidadeResposta(bool Disponivel, decimal PrecoUnitario);
