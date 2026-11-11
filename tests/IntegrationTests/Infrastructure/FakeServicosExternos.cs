using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace IntegrationTests.Infrastructure;

/// <summary>
/// Faz o papel dos serviços Cadastro e PecasInsumos, que não existem mais no processo da OS:
/// atende por <see cref="HttpMessageHandler"/> as mesmas rotas e formatos de resposta que os
/// gateways HTTP consomem (GET de clientes, veículos e serviços do Cadastro; GET de peça e de
/// disponibilidade do PecasInsumos). Os testes cadastram as entidades que
/// precisam por <c>Adicionar*</c> e consultam as requisições recebidas por <see cref="Requisicoes"/>.
/// </summary>
public sealed partial class FakeServicosExternos : HttpMessageHandler
{
    private sealed record Cliente(Guid Id, string Nome, string Email, bool Ativo);
    private sealed record Veiculo(Guid Id, string Placa, Guid ClienteId);
    private sealed record Servico(Guid Id, string Nome, decimal Preco);
    private sealed record Peca(Guid Id, string Nome, decimal Preco, int Estoque, string Unidade);

    /// <summary>Requisição recebida pelo fake, com o header Authorization que chegou.</summary>
    public sealed record RequisicaoRecebida(string Metodo, string CaminhoEQuery, AuthenticationHeaderValue? Authorization);

    private readonly ConcurrentDictionary<Guid, Cliente> _clientes = new();
    private readonly ConcurrentDictionary<Guid, Veiculo> _veiculos = new();
    private readonly ConcurrentDictionary<Guid, Servico> _servicos = new();
    private readonly ConcurrentDictionary<Guid, Peca> _pecas = new();
    private readonly ConcurrentQueue<RequisicaoRecebida> _requisicoes = new();

    // Clientes cujo Cadastro "cai" depois de N consultas bem-sucedidas: guarda quantas ainda passam.
    private readonly ConcurrentDictionary<Guid, int> _consultasRestantesDoCliente = new();

    public IReadOnlyCollection<RequisicaoRecebida> Requisicoes => _requisicoes;

    /// <summary>
    /// Simula o Cadastro saindo do ar para o cliente: as primeiras <paramref name="consultasComSucesso"/>
    /// consultas a <c>GET /api/v1/clientes/{id}</c> funcionam e as seguintes respondem 503. Permite
    /// derrubar o Cadastro entre a validação da abertura e a montagem do e-mail do orçamento.
    /// </summary>
    public void SimularCadastroIndisponivelApos(Guid clienteId, int consultasComSucesso) =>
        _consultasRestantesDoCliente[clienteId] = consultasComSucesso;

    private bool CadastroIndisponivelPara(Guid clienteId)
    {
        if (!_consultasRestantesDoCliente.TryGetValue(clienteId, out _)) return false;

        var restantes = _consultasRestantesDoCliente.AddOrUpdate(clienteId, 0, (_, atual) => atual - 1);
        return restantes < 0;
    }

    public Guid AdicionarCliente(string nome, string email, bool ativo = true)
    {
        var id = Guid.NewGuid();
        _clientes[id] = new Cliente(id, nome, email, ativo);
        return id;
    }

    public Guid AdicionarVeiculo(string placa, Guid clienteId)
    {
        var id = Guid.NewGuid();
        _veiculos[id] = new Veiculo(id, placa, clienteId);
        return id;
    }

    public Guid AdicionarServico(string nome, decimal preco)
    {
        var id = Guid.NewGuid();
        _servicos[id] = new Servico(id, nome, preco);
        return id;
    }

    public Guid AdicionarPeca(string nome, decimal preco, int estoque, string unidade)
    {
        var id = Guid.NewGuid();
        _pecas[id] = new Peca(id, nome, preco, estoque, unidade);
        return id;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        _requisicoes.Enqueue(new RequisicaoRecebida(
            request.Method.Method, uri.PathAndQuery, request.Headers.Authorization));

        if (request.Method != HttpMethod.Get)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));

        var caminho = uri.AbsolutePath;

        if (ClientePorId().Match(caminho) is { Success: true } mCliente)
        {
            if (CadastroIndisponivelPara(IdDe(mCliente)))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            return Responder(_clientes.GetValueOrDefault(IdDe(mCliente)), c => new
            {
                c.Id, c.Nome, Documento = "00000000000", c.Email, Telefone = "31999990000", c.Ativo,
                CadastradoEm = DateTime.UtcNow, AtualizadoEm = DateTime.UtcNow
            });
        }

        if (VeiculoPorId().Match(caminho) is { Success: true } mVeiculo)
            return Responder(_veiculos.GetValueOrDefault(IdDe(mVeiculo)), v => new
            {
                v.Id, v.Placa, Modelo = "Modelo", Marca = "Marca", Ano = 2020, v.ClienteId,
                CadastradoEm = DateTime.UtcNow, AtualizadoEm = DateTime.UtcNow
            });

        if (ServicoPorId().Match(caminho) is { Success: true } mServico)
            return Responder(_servicos.GetValueOrDefault(IdDe(mServico)), s => new
            {
                s.Id, s.Nome, Descricao = (string?)null, s.Preco, Ativo = true,
                CadastradoEm = DateTime.UtcNow, AtualizadoEm = DateTime.UtcNow
            });

        if (DisponibilidadeDaPeca().Match(caminho) is { Success: true } mDisp)
        {
            var quantidade = int.Parse(
                System.Web.HttpUtility.ParseQueryString(uri.Query)["quantidade"] ?? "0");
            return Responder(_pecas.GetValueOrDefault(IdDe(mDisp)), p => new
            {
                Disponivel = p.Estoque >= quantidade, PrecoUnitario = p.Preco
            });
        }

        if (PecaPorId().Match(caminho) is { Success: true } mPeca)
            return Responder(_pecas.GetValueOrDefault(IdDe(mPeca)), p => new
            {
                p.Id, p.Nome, Descricao = (string?)null, PrecoUnitario = p.Preco,
                QuantidadeEmEstoque = p.Estoque, UnidadeDeMedida = p.Unidade, Ativo = true,
                CadastradoEm = DateTime.UtcNow, AtualizadoEm = DateTime.UtcNow
            });

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Guid IdDe(Match match) => Guid.Parse(match.Groups["id"].Value);

    private static Task<HttpResponseMessage> Responder<T>(T? entidade, Func<T, object> corpo)
        where T : class =>
        Task.FromResult(entidade is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(corpo(entidade)) });

    [GeneratedRegex(@"^/api/v1/clientes/(?<id>[0-9a-fA-F-]{36})$")]
    private static partial Regex ClientePorId();

    [GeneratedRegex(@"^/api/v1/veiculos/(?<id>[0-9a-fA-F-]{36})$")]
    private static partial Regex VeiculoPorId();

    [GeneratedRegex(@"^/api/v1/servicos/(?<id>[0-9a-fA-F-]{36})$")]
    private static partial Regex ServicoPorId();

    [GeneratedRegex(@"^/api/v1/pecas-insumos/(?<id>[0-9a-fA-F-]{36})$")]
    private static partial Regex PecaPorId();

    [GeneratedRegex(@"^/api/v1/pecas-insumos/(?<id>[0-9a-fA-F-]{36})/disponibilidade$")]
    private static partial Regex DisponibilidadeDaPeca();
}
