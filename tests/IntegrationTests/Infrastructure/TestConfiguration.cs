namespace IntegrationTests.Infrastructure;

/// <summary>
/// Configuração comum aos hosts de teste (ambiente "Testing"). As URLs dos serviços Cadastro e
/// PecasInsumos são fictícias: nenhuma requisição sai do processo, o handler primário dos
/// HttpClients é substituído por <see cref="FakeServicosExternos"/> (ou nunca é acionado).
/// </summary>
internal static class TestConfiguration
{
    public const string JwtSecret = "integration-test-jwt-secret-minimum-32-chars!!";

    // Mesmos valores usados para gerar e validar os tokens — ver RFC-001 §4.1 (contrato do
    // token) e ADR-003 (dois emissores).
    public const string AppIssuer = "oficina-mecanica-app";
    public const string ClienteAuthIssuer = "oficina-mecanica-auth";
    public const string Audience = "oficina-mecanica-api";

    public static Dictionary<string, string?> Valores(string connectionString) => new()
    {
        ["ConnectionStrings:Default"] = connectionString,
        ["Jwt:Secret"] = JwtSecret,
        ["Jwt:Audience"] = Audience,
        ["Jwt:ValidIssuers:0"] = AppIssuer,
        ["Jwt:ValidIssuers:1"] = ClienteAuthIssuer,
        // Mensageria em memória: os testes de integração observam as publicações pelo test harness.
        ["Mensageria:Transporte"] = "InMemory",
        ["Servicos:Cadastro:BaseUrl"] = "http://cadastro.test",
        ["Servicos:PecasInsumos:BaseUrl"] = "http://pecas-insumos.test",
        // A exposição do OpenAPI/Scalar não depende do ambiente (ver Api.Extensions.OpenApiExtensions),
        // então é desligada explicitamente para manter a suíte rápida e sem rotas de documentação.
        ["OpenApi:Enabled"] = "false",
    };
}
