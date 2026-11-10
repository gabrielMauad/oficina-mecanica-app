using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IntegrationTests.Infrastructure;

namespace IntegrationTests.Modules.OrdemServico;

/// <summary>
/// Trava o acoplamento por string entre <c>OrdemServicoErrors.AcessoNegado.Code</c>
/// (OrdensServico.Application, <c>internal</c>) e a constante <c>AcessoNegadoErrorCode</c>
/// duplicada no <c>OrdemServicoApiController</c>.
///
/// O record <c>SharedKernel.Domain.Error</c> não carrega categoria — <c>Error.Forbidden</c>,
/// <c>Error.NotFound</c> e <c>Error.Validation</c> produzem exatamente a mesma coisa — então o
/// 403 é decidido comparando o código do erro com essa constante. Renomear o código do erro em
/// um lado sem o outro faria o 403 virar 404 ou 422 silenciosamente, sem quebrar o build.
///
/// Estes testes cobrem os dois pontos da regra de dono (RFC-001 §4.2) ainda não cobertos por
/// <see cref="OrdemServicoEndpointsTests"/> — que já cobre <c>GET /{id}</c> e
/// <c>PATCH /{id}/aprovar-orcamento</c> — e verificam também o código do erro no corpo da
/// resposta, que é o elo entre as duas camadas.
/// </summary>
[Collection("Integration")]
public class OrdemServicoAcessoDoDonoTests
{
    private const string AcessoNegadoErrorCode = "OrdemServico.AcessoNegado";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OficinaMecanicaWebApplicationFactory _factory;

    public OrdemServicoAcessoDoDonoTests(OficinaMecanicaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static HttpContent EmptyJsonContent() =>
        new StringContent("{}", Encoding.UTF8, "application/json");

    [Fact(DisplayName = "GET /ordens-servico/{id}/status — token de outro cliente retorna 403 (não 404) com código OrdemServico.AcessoNegado")]
    public async Task ObterStatus_ComTokenDeOutroCliente_DeveRetornar403()
    {
        // Arrange — a oficina cria a OS de um dono
        var token = await _factory.GetAuthTokenAsync();
        using var oficinaClient = _factory.CreateAuthenticatedClient(token);

        var donoId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Dono do Status"), email: TestData.Email("dono.status"));

        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: donoId);

        var osId = await CriarOrdemServicoAsync(oficinaClient, donoId, veiculoId);

        // Act — outro cliente (id aleatório, não é o dono) consulta o status da OS
        using var outroClienteClient = _factory.CreateClienteAuthenticatedClient(Guid.NewGuid());
        var response = await outroClienteClient.GetAsync($"/api/v1/ordens-servico/{osId}/status");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(AcessoNegadoErrorCode, await LerCodigoDoErroAsync(response));
    }

    [Fact(DisplayName = "PATCH /ordens-servico/{id}/rejeitar-orcamento — token de outro cliente retorna 403 (não 422) com código OrdemServico.AcessoNegado")]
    public async Task RejeitarOrcamento_ComTokenDeOutroCliente_DeveRetornar403()
    {
        // Arrange — a oficina cria a OS e gera o orçamento
        var token = await _factory.GetAuthTokenAsync();
        using var oficinaClient = _factory.CreateAuthenticatedClient(token);

        var donoId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Dono da Rejeicao"), email: TestData.Email("dono.rejeicao"));

        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: donoId);

        var servicoId = _factory.ServicosExternos.AdicionarServico(nome: TestData.Nome("Alinhamento Acesso Dono"), preco: 180.00m);

        var pecaId = _factory.ServicosExternos.AdicionarPeca(nome: TestData.Nome("Contrapeso de Roda Acesso Dono"), preco: 25.00m, estoque: 20, unidade: "Unidade");

        var osId = await CriarOrdemServicoAsync(oficinaClient, donoId, veiculoId);

        await oficinaClient.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/iniciar-diagnostico", EmptyJsonContent());
        await oficinaClient.PatchAsJsonAsync(
            $"/api/v1/ordens-servico/{osId}/registrar-diagnostico", new
            {
                DescricaoDiagnostico = "Pneus desalinhados.",
                Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
                Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 2 } }
            });

        // Act — outro cliente (não é o dono) tenta rejeitar o orçamento
        using var outroClienteClient = _factory.CreateClienteAuthenticatedClient(Guid.NewGuid());
        var response = await outroClienteClient.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/rejeitar-orcamento", EmptyJsonContent());

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(AcessoNegadoErrorCode, await LerCodigoDoErroAsync(response));
    }

    // ── Helpers ──

    /// <summary>
    /// Lê o campo "code" do <c>Error</c> serializado no corpo da resposta de falha.
    /// </summary>
    private static async Task<string?> LerCodigoDoErroAsync(HttpResponseMessage response)
    {
        var erro = await response.Content.ReadFromJsonAsync<ErroDto>(JsonOptions);
        return erro?.Code;
    }

    private async Task<Guid> CriarOrdemServicoAsync(HttpClient client, Guid clienteId, Guid veiculoId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OrdemServicoIdDto>(JsonOptions);
        return body!.Id;
    }

    // ── DTOs locais para desserialização ──

    private sealed record ErroDto(string Code, string Message);
    private sealed record OrdemServicoIdDto(Guid Id);
}
