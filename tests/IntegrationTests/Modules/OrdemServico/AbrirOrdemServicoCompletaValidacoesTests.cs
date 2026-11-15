using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTests.Infrastructure;

namespace IntegrationTests.Modules.OrdemServico;

/// <summary>
/// Recusas síncronas da abertura de OS que dependem do que Cadastro e PecasInsumos respondem
/// (serviço e peça desativados) e dos limites de quantidade e de valor do orçamento.
/// </summary>
[Collection("Integration")]
public class AbrirOrdemServicoCompletaValidacoesTests
{
    private readonly OficinaMecanicaWebApplicationFactory _factory;

    public AbrirOrdemServicoCompletaValidacoesTests(OficinaMecanicaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Cenario(Guid ClienteId, Guid VeiculoId);

    private Cenario CriarClienteComVeiculo(string rotulo)
    {
        var clienteId = _factory.ServicosExternos.AdicionarCliente(
            nome: TestData.Nome($"Cliente {rotulo}"), email: TestData.Email(rotulo.ToLowerInvariant()));
        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(
            placa: TestData.PlacaMercosul(), clienteId: clienteId);
        return new Cenario(clienteId, veiculoId);
    }

    private async Task<(HttpStatusCode Status, string Codigo)> AbrirAsync(
        Cenario c, Guid servicoId, int quantidadeServico, Guid pecaId, int quantidadePeca)
    {
        using var client = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var response = await client.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            c.ClienteId,
            c.VeiculoId,
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = quantidadeServico } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = quantidadePeca } }
        });

        var corpo = await response.Content.ReadAsStringAsync();
        var codigo = "";
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            using var json = JsonDocument.Parse(corpo);
            codigo = json.RootElement.TryGetProperty("code", out var code) ? code.GetString()! : "validation";
        }

        return (response.StatusCode, codigo);
    }

    [Fact(DisplayName = "POST /completa: serviço desativado no Cadastro → 422 ServicoDesativado")]
    public async Task AbrirCompleta_ServicoDesativado_Retorna422()
    {
        var c = CriarClienteComVeiculo("ServicoDesativado");
        var servicoId = _factory.ServicosExternos.AdicionarServico(
            TestData.Nome("Serviço Desativado"), preco: 80m, ativo: false);
        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            TestData.Nome("Peça Ativa"), preco: 10m, estoque: 10, unidade: "Unidade");

        var (status, codigo) = await AbrirAsync(c, servicoId, 1, pecaId, 1);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal("OrdemServico.ServicoDesativado", codigo);
    }

    [Fact(DisplayName = "POST /completa: peça desativada (PecasInsumos responde indisponível) → 422 PecaIndisponivel")]
    public async Task AbrirCompleta_PecaDesativada_Retorna422()
    {
        var c = CriarClienteComVeiculo("PecaDesativada");
        var servicoId = _factory.ServicosExternos.AdicionarServico(TestData.Nome("Serviço Ativo"), preco: 80m);
        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            TestData.Nome("Peça Desativada"), preco: 10m, estoque: 10, unidade: "Unidade", ativa: false);

        var (status, codigo) = await AbrirAsync(c, servicoId, 1, pecaId, 1);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal("OrdemServico.PecaIndisponivel", codigo);
    }

    [Theory(DisplayName = "POST /completa: quantidade de serviço fora de 1..999 → 422 (nunca 500)")]
    [InlineData(int.MaxValue)]
    [InlineData(1000)]
    [InlineData(0)]
    public async Task AbrirCompleta_QuantidadeDeServicoForaDaFaixa_Retorna422(int quantidade)
    {
        var c = CriarClienteComVeiculo("QtdServico");
        var servicoId = _factory.ServicosExternos.AdicionarServico(TestData.Nome("Serviço Qtd"), preco: 100m);
        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            TestData.Nome("Peça Qtd"), preco: 10m, estoque: 10, unidade: "Unidade");

        var (status, _) = await AbrirAsync(c, servicoId, quantidade, pecaId, 1);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
    }

    [Fact(DisplayName = "POST /completa: quantidade de peça int.MaxValue → 422 (nunca 500)")]
    public async Task AbrirCompleta_QuantidadeDePecaIntMaxValue_Retorna422()
    {
        var c = CriarClienteComVeiculo("QtdPeca");
        var servicoId = _factory.ServicosExternos.AdicionarServico(TestData.Nome("Serviço Qtd Peça"), preco: 100m);
        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            TestData.Nome("Peça Qtd Peça"), preco: 10m, estoque: int.MaxValue, unidade: "Unidade");

        var (status, _) = await AbrirAsync(c, servicoId, 1, pecaId, int.MaxValue);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
    }

    [Fact(DisplayName = "POST /completa: total acima do que a coluna comporta → 422 ValorTotalExcedido (nunca 500)")]
    public async Task AbrirCompleta_TotalAcimaDoLimite_Retorna422()
    {
        var c = CriarClienteComVeiculo("TotalExcedido");
        var servicoId = _factory.ServicosExternos.AdicionarServico(
            TestData.Nome("Serviço Caro"), preco: 99_999_999m);
        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            TestData.Nome("Peça Barata"), preco: 10m, estoque: 10, unidade: "Unidade");

        var (status, codigo) = await AbrirAsync(c, servicoId, 999, pecaId, 1);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal("Orcamento.ValorTotalExcedido", codigo);
    }
}
