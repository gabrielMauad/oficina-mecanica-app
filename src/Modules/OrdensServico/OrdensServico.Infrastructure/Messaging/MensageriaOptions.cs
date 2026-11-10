namespace OrdensServico.Infrastructure.Messaging;

/// <summary>
/// Configuração da mensageria (MassTransit), chaves <c>Mensageria__*</c>: transporte
/// (<c>AmazonSqs</c> na AWS e no LocalStack, <c>InMemory</c> em testes), região, URL do serviço
/// (LocalStack) e credenciais do ambiente local.
/// </summary>
public sealed class MensageriaOptions
{
    public const string SectionName = "Mensageria";
    public const string TransporteAmazonSqs = "AmazonSqs";
    public const string TransporteInMemory = "InMemory";

    public string Transporte { get; set; } = TransporteAmazonSqs;
    public string Regiao { get; set; } = "us-east-1";

    /// <summary>Vazio na AWS; no ambiente local aponta para o LocalStack (SQS e SNS).</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Só no ambiente local; vazio na AWS (cadeia padrão de credenciais do SDK).</summary>
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}
