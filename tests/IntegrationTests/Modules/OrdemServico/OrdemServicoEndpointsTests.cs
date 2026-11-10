using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IntegrationTests.Infrastructure;

namespace IntegrationTests.Modules.OrdemServico;

[Collection("Integration")]
public class OrdemServicoEndpointsTests
{
    private readonly OficinaMecanicaWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// HttpContent é descartável e consumido após cada request.
    /// Use este método para obter uma nova instância a cada chamada.
    /// </summary>
    private static HttpContent EmptyJsonContent() =>
        new StringContent("{}", Encoding.UTF8, "application/json");

    public OrdemServicoEndpointsTests(OficinaMecanicaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // =========================================================================
    // Fluxo completo: POST → GET → PATCH (todos os estados)
    // =========================================================================

    [Fact(DisplayName = "Fluxo completo de OS: Recebida → Entregue (11 requests)")]
    public async Task FluxoCompleto_DevePersistirTodosOsEstados()
    {
        // Arrange — criar todos os pré-requisitos
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        // 1. Criar Cliente
        var clienteId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Carlos Teste OS"), email: TestData.Email("carlos.os"));

        // 2. Criar Veículo (vinculado ao cliente)
        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: clienteId);

        // 3. Criar Serviço no catálogo
        var servicoId = _factory.ServicosExternos.AdicionarServico(nome: TestData.Nome("Troca de Óleo OS"), preco: 150.00m);

        // 4. Criar Peça/Insumo com estoque
        var pecaId = _factory.ServicosExternos.AdicionarPeca(nome: TestData.Nome("Óleo 5W30 1L"), preco: 35.00m, estoque: 50, unidade: "Litro");

        // ── Ciclo de vida da OS ──

        // 5. Criar OS
        var osResponse = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId
        });
        Assert.Equal(HttpStatusCode.Created, osResponse.StatusCode);

        var os = await osResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        Assert.NotNull(os);
        var osId = os.Id;
        Assert.Equal("Recebida", os.Status);
        Assert.Equal(clienteId, os.ClienteId);
        Assert.Equal(veiculoId, os.VeiculoId);

        // 6. Iniciar Diagnóstico: Recebida → EmDiagnostico
        var iniciarResponse = await client.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/iniciar-diagnostico", EmptyJsonContent());
        Assert.Equal(HttpStatusCode.OK, iniciarResponse.StatusCode);

        var osAposIniciar = await iniciarResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        Assert.Equal("EmDiagnostico", osAposIniciar!.Status);

        // 7. Registrar Diagnóstico
        //    O handler EnviarOrcamentoAoCliente reage ao evento OrcamentoGerado
        //    e move a OS de forma síncrona para AguardandoAprovacao + orçamento para Enviado
        var registrarBody = new
        {
            DescricaoDiagnostico = "Motor com desgaste. Necessária troca de óleo.",
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 2 } }
        };

        var registrarResponse = await client.PatchAsJsonAsync(
            $"/api/v1/ordens-servico/{osId}/registrar-diagnostico", registrarBody);
        Assert.Equal(HttpStatusCode.OK, registrarResponse.StatusCode);

        // Verificar via GET: o handler já processou o evento → AguardandoAprovacao
        var osAposRegistrar = await ObterOsAsync(client, osId);
        Assert.Equal("AguardandoAprovacao", osAposRegistrar.Status);
        Assert.NotEmpty(osAposRegistrar.Orcamentos);
        Assert.Equal("Enviado", osAposRegistrar.Orcamentos[0].Status);
        Assert.NotNull(osAposRegistrar.Orcamentos[0].DataEnvio);

        // 8. Aprovar Orçamento: orçamento muda para Aprovado, OS permanece AguardandoAprovacao
        //    Rota exige papel Cliente (RFC-001 §4.2) — usa um client autenticado como o dono da OS
        using var clienteClient = _factory.CreateClienteAuthenticatedClient(clienteId);
        var aprovarResponse = await clienteClient.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/aprovar-orcamento", EmptyJsonContent());
        Assert.Equal(HttpStatusCode.OK, aprovarResponse.StatusCode);

        var osAposAprovar = await aprovarResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        Assert.NotNull(osAposAprovar);
        Assert.Equal("AguardandoAprovacao", osAposAprovar.Status); // OS não avança, só orçamento
        Assert.Equal("Aprovado", osAposAprovar.Orcamentos[0].Status);
        Assert.NotNull(osAposAprovar.Orcamentos[0].DataAprovacao);

        // 9. Executar: AguardandoAprovacao → EmExecucao (requer orçamento Aprovado)
        var executarResponse = await client.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/executar", EmptyJsonContent());
        Assert.Equal(HttpStatusCode.OK, executarResponse.StatusCode);

        var osAposExecutar = await executarResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        Assert.Equal("EmExecucao", osAposExecutar!.Status);

        // 10. Finalizar: EmExecucao → Finalizada
        //     Handler NotificarClienteAoFinalizar preenche NotificadoEm de forma síncrona
        var finalizarResponse = await client.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/finalizar", EmptyJsonContent());
        Assert.Equal(HttpStatusCode.OK, finalizarResponse.StatusCode);

        var osAposFinalizar = await finalizarResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        Assert.Equal("Finalizada", osAposFinalizar!.Status);

        // 11. Concluir: Finalizada → Entregue
        var concluirResponse = await client.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/concluir", EmptyJsonContent());
        Assert.Equal(HttpStatusCode.OK, concluirResponse.StatusCode);

        // 12. Verificar estado final via GET (autenticado — GET /{id} requer autorização)
        var osFinal = await ObterOsAsync(client, osId);

        Assert.Equal("Entregue", osFinal.Status);
        Assert.NotNull(osFinal.EntregueEm);
        Assert.NotNull(osFinal.NotificadoEm);
        Assert.Equal("Motor com desgaste. Necessária troca de óleo.", osFinal.DescricaoDiagnostico);
        Assert.Single(osFinal.ItensServico);
        Assert.Single(osFinal.ItensPeca);
        Assert.Equal(150.00m, osFinal.ItensServico[0].PrecoUnitarioSnapshot);
        Assert.Equal(35.00m, osFinal.ItensPeca[0].PrecoUnitarioSnapshot);
        Assert.Equal("Aprovado", osFinal.Orcamentos[0].Status);
    }

    // =========================================================================
    // PATCH /api/v1/ordens-servico/{id}/rejeitar-orcamento
    // =========================================================================

    [Fact(DisplayName = "Rejeitar orçamento: orçamento fica Rejeitado; OS permanece AguardandoAprovacao")]
    public async Task RejeitarOrcamento_DeveMudarStatusOrcamentoParaRejeitado()
    {
        // Arrange
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        var clienteId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Rejeita OS Cliente"), email: TestData.Email("rejeita.os"));

        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: clienteId);

        var servicoId = _factory.ServicosExternos.AdicionarServico(nome: TestData.Nome("Revisão Completa Rejeita"), preco: 200.00m);

        var pecaId = _factory.ServicosExternos.AdicionarPeca(nome: TestData.Nome("Filtro Ar K&N"), preco: 80.00m, estoque: 10, unidade: "Unidade");

        // Criar OS e avançar até AguardandoAprovacao (estado necessário para rejeitar)
        var osResponse = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId
        });
        osResponse.EnsureSuccessStatusCode();
        var os = await osResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        var osId = os!.Id;

        await client.PatchAsync($"/api/v1/ordens-servico/{osId}/iniciar-diagnostico", EmptyJsonContent());

        await client.PatchAsJsonAsync($"/api/v1/ordens-servico/{osId}/registrar-diagnostico", new
        {
            DescricaoDiagnostico = "Revisão geral necessária.",
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 1 } }
        });

        // Precondição: OS em AguardandoAprovacao com orçamento Enviado
        var osAntes = await ObterOsAsync(client, osId);
        Assert.Equal("AguardandoAprovacao", osAntes.Status);
        Assert.Equal("Enviado", osAntes.Orcamentos[0].Status);

        // Act — Rejeitar orçamento (rota exige papel Cliente — RFC-001 §4.2)
        using var clienteClient = _factory.CreateClienteAuthenticatedClient(clienteId);
        var rejeitarResponse = await clienteClient.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/rejeitar-orcamento", EmptyJsonContent());

        // Assert
        Assert.Equal(HttpStatusCode.OK, rejeitarResponse.StatusCode);

        var osAposRejeitar = await rejeitarResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        Assert.NotNull(osAposRejeitar);
        // O status da OS permanece AguardandoAprovacao — apenas o orçamento muda
        Assert.Equal("AguardandoAprovacao", osAposRejeitar.Status);
        Assert.Equal("Rejeitado", osAposRejeitar.Orcamentos[0].Status);
    }

    // =========================================================================
    // GET /api/v1/ordens-servico?clienteId={id} — papel Oficina (RFC-001 §7: era
    // AllowAnonymous na Fase 2; decisão desta fase foi protegê-la — ver "Questões em aberto")
    // =========================================================================

    [Fact(DisplayName = "GET /ordens-servico?clienteId — com token Oficina, retorna lista do cliente")]
    public async Task ListarOrdensPorCliente_ComTokenOficina_DeveRetornar200()
    {
        // Arrange — criar OS com autenticação
        var token = await _factory.GetAuthTokenAsync();
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var clienteId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Cliente Lista Oficina"), email: TestData.Email("lista.oficina"));

        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: clienteId);

        var osResponse = await authClient.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId
        });
        osResponse.EnsureSuccessStatusCode();
        var os = await osResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);

        // Act
        var response = await authClient.GetAsync($"/api/v1/ordens-servico?clienteId={clienteId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var lista = await response.Content.ReadFromJsonAsync<List<OrdemServicoDto>>(JsonOptions);
        Assert.NotNull(lista);
        Assert.NotEmpty(lista);
        Assert.Contains(lista, x => x.Id == os!.Id);
        Assert.All(lista, x => Assert.Equal(clienteId, x.ClienteId));
    }

    [Fact(DisplayName = "GET /ordens-servico?clienteId — sem token, retorna 401")]
    public async Task ListarOrdensPorCliente_SemToken_DeveRetornar401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/ordens-servico?clienteId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "GET /ordens-servico?clienteId — token Cliente, retorna 403 (rota exige Oficina)")]
    public async Task ListarOrdensPorCliente_ComTokenCliente_DeveRetornar403()
    {
        using var clienteClient = _factory.CreateClienteAuthenticatedClient(Guid.NewGuid());

        var response = await clienteClient.GetAsync($"/api/v1/ordens-servico?clienteId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Isolamento entre clientes — RFC-001 §4.2: um token Cliente só opera sobre a
    // própria OS, comparando o `sub` do token com o dono da OS.
    // =========================================================================

    [Fact(DisplayName = "GET /ordens-servico/{id} — token de outro cliente retorna 403")]
    public async Task ObterOS_ComTokenDeOutroCliente_DeveRetornar403()
    {
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        var donoId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Dono da OS"), email: TestData.Email("dono.os"));

        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: donoId);

        var osResponse = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = donoId,
            VeiculoId = veiculoId
        });
        osResponse.EnsureSuccessStatusCode();
        var osId = (await osResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions))!.Id;

        // Act — outro cliente (id aleatório, não é o dono) tenta acessar a OS
        using var outroClienteClient = _factory.CreateClienteAuthenticatedClient(Guid.NewGuid());
        var response = await outroClienteClient.GetAsync($"/api/v1/ordens-servico/{osId}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "PATCH /aprovar-orcamento — token de outro cliente retorna 403")]
    public async Task AprovarOrcamento_ComTokenDeOutroCliente_DeveRetornar403()
    {
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        var donoId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Dono do Orcamento"), email: TestData.Email("dono.orcamento"));

        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: donoId);

        var servicoId = _factory.ServicosExternos.AdicionarServico(nome: TestData.Nome("Troca de Amortecedor"), preco: 300.00m);

        var pecaId = _factory.ServicosExternos.AdicionarPeca(nome: TestData.Nome("Amortecedor Traseiro"), preco: 150.00m, estoque: 5, unidade: "Unidade");

        var osResponse = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = donoId,
            VeiculoId = veiculoId
        });
        osResponse.EnsureSuccessStatusCode();
        var osId = (await osResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions))!.Id;

        await client.PatchAsync($"/api/v1/ordens-servico/{osId}/iniciar-diagnostico", EmptyJsonContent());
        await client.PatchAsJsonAsync($"/api/v1/ordens-servico/{osId}/registrar-diagnostico", new
        {
            DescricaoDiagnostico = "Amortecedor com vazamento.",
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 1 } }
        });

        // Act — outro cliente (não é o dono) tenta aprovar o orçamento
        using var outroClienteClient = _factory.CreateClienteAuthenticatedClient(Guid.NewGuid());
        var response = await outroClienteClient.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/aprovar-orcamento", EmptyJsonContent());

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // POST /api/v1/ordens-servico — sem autenticação → 401
    // =========================================================================

    [Fact(DisplayName = "POST /ordens-servico sem token — retorna 401")]
    public async Task CriarOS_SemAutenticacao_DeveRetornar401()
    {
        // Arrange — client sem token
        using var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = Guid.NewGuid(),
            VeiculoId = Guid.NewGuid()
        });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // GET /api/v1/ordens-servico/{id} — sem autenticação → 401
    // (ObterPorId NÃO tem AllowAnonymous — ao contrário do ListarPorCliente)
    // =========================================================================

    [Fact(DisplayName = "GET /ordens-servico/{id} sem token — retorna 401")]
    public async Task ObterOS_SemAutenticacao_DeveRetornar401()
    {
        // Arrange — client sem token
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/v1/ordens-servico/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // GET /api/v1/ordens-servico/{id} — OS não existe → 404
    // =========================================================================

    [Fact(DisplayName = "GET /ordens-servico/{id} inexistente — retorna 404")]
    public async Task ObterOS_NaoEncontrada_DeveRetornar404()
    {
        // Arrange
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        // Act
        var response = await client.GetAsync($"/api/v1/ordens-servico/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // PATCH /{id}/iniciar-diagnostico — transição inválida → 422
    // =========================================================================

    [Fact(DisplayName = "PATCH /iniciar-diagnostico em OS já EmDiagnostico — retorna 422 (transição inválida)")]
    public async Task IniciarDiagnostico_TransicaoInvalida_DeveRetornar422()
    {
        // Arrange
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        var clienteId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Transicao Invalida"), email: TestData.Email("transicao.invalida"));

        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: clienteId);

        var osResponse = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId
        });
        osResponse.EnsureSuccessStatusCode();
        var os = await osResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        var osId = os!.Id;

        // Avançar para EmDiagnostico (primeira chamada válida)
        var primeiraVez = await client.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/iniciar-diagnostico", EmptyJsonContent());
        Assert.Equal(HttpStatusCode.OK, primeiraVez.StatusCode);

        // Act — tentar iniciar novamente (OS não está mais em Recebida)
        var segundaVez = await client.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/iniciar-diagnostico", EmptyJsonContent());

        // Assert — domínio rejeita a transição inválida
        Assert.Equal(HttpStatusCode.UnprocessableEntity, segundaVez.StatusCode);
    }

    // ── Helpers privados ──

    private async Task<OrdemServicoDto> ObterOsAsync(HttpClient client, Guid osId)
    {
        var response = await client.GetAsync($"/api/v1/ordens-servico/{osId}");
        response.EnsureSuccessStatusCode();
        var os = await response.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        return os!;
    }

    // ── DTOs locais para desserialização ──


    private sealed record OrdemServicoDto(
        Guid Id,
        Guid ClienteId,
        Guid VeiculoId,
        string Status,
        string? DescricaoDiagnostico,
        DateTime? NotificadoEm,
        DateTime? EntregueEm,
        DateTime CriadoEm,
        DateTime AtualizadoEm,
        ItemServicoDto[] ItensServico,
        ItemPecaDto[] ItensPeca,
        OrcamentoDto[] Orcamentos);

    private sealed record ItemServicoDto(
        Guid ServicoId,
        int Quantidade,
        decimal PrecoUnitarioSnapshot);

    private sealed record ItemPecaDto(
        Guid PecaInsumoId,
        int Quantidade,
        decimal PrecoUnitarioSnapshot);

    private sealed record OrcamentoDto(
        decimal ValorTotal,
        string Status,
        DateTime DataGeracao,
        DateTime? DataEnvio,
        DateTime? DataAprovacao);
}
