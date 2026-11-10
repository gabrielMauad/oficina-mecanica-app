namespace OrdensServico.Infrastructure.Http;

/// <summary>
/// URLs base dos serviços consultados por HTTP. Chaves: <c>Servicos__Cadastro__BaseUrl</c> e
/// <c>Servicos__PecasInsumos__BaseUrl</c> (seção <c>Servicos</c> do appsettings ou variáveis de
/// ambiente), obrigatórias e validadas na partida.
/// </summary>
public sealed class ServicosOptions
{
    public const string SectionName = "Servicos";

    public ServicoRemotoOptions Cadastro { get; set; } = new();
    public ServicoRemotoOptions PecasInsumos { get; set; } = new();
}

public sealed class ServicoRemotoOptions
{
    public string? BaseUrl { get; set; }
}
