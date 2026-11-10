namespace IntegrationTests.Infrastructure;

/// <summary>
/// Gerador de fixtures únicas para os testes de integração.
///
/// A coleção "Integration" compartilha um único PostgreSQL por sessão e nunca limpa os dados
/// entre testes; nomes, e-mails e placas únicos evitam que um teste dependa do que outro deixou
/// no banco ou no fake dos serviços externos.
/// </summary>
public static class TestData
{
    private static readonly char[] Letras = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    /// <summary>
    /// Sufixo curto derivado de <see cref="Guid"/>, usado para tornar nomes e descrições
    /// únicos sem deixá-los ilegíveis no relatório de falha.
    /// </summary>
    public static string Sufixo() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Sufixo curto derivado de <see cref="Guid"/> contendo apenas letras.
    /// </summary>
    public static string SufixoLetras()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return new string(bytes.Take(8).Select(b => (char)('a' + b % 26)).ToArray());
    }

    /// <summary>
    /// Nome único: o rótulo informado seguido de um sufixo só de letras
    /// (ex.: "João Silva kqzmbvxa").
    /// </summary>
    public static string Nome(string rotulo) => $"{rotulo} {SufixoLetras()}";

    /// <summary>
    /// Descrição única: o texto informado seguido de um sufixo curto.
    /// </summary>
    public static string Descricao(string texto) => $"{texto} {Sufixo()}";

    /// <summary>
    /// E-mail único no domínio de testes (ex.: "joao.silva.a1b2c3d4@integration.test").
    /// </summary>
    public static string Email(string prefixo) => $"{prefixo}.{Sufixo()}@integration.test";

    /// <summary>
    /// Placa única no formato Mercosul (ABC1D23).
    /// </summary>
    public static string PlacaMercosul() =>
        $"{Letra()}{Letra()}{Letra()}{Digito()}{Letra()}{Digito()}{Digito()}";

    private static char Letra() => Letras[Random.Shared.Next(Letras.Length)];

    private static char Digito() => (char)('0' + Random.Shared.Next(10));
}
