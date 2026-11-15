using SharedKernel.Domain;

namespace OrdensServico.Domain.OrdemServico;

/// <summary>
/// Limites do orçamento: mantêm o total dentro do que a coluna <c>valor_total</c>
/// (<c>numeric(10,2)</c>) consegue armazenar e a quantidade de cada item em uma faixa operacional.
/// </summary>
public static class LimitesDoOrcamento
{
    /// <summary>Maior quantidade aceita em um item de serviço ou de peça.</summary>
    public const int QuantidadeMaximaPorItem = 999;

    /// <summary>Maior valor total de orçamento: o máximo de um <c>numeric(10,2)</c>.</summary>
    public const decimal ValorTotalMaximo = 99_999_999.99m;

    internal static readonly Error ValorTotalExcedido = Error.Validation(
        "Orcamento.ValorTotalExcedido",
        $"O valor total do orçamento não pode ultrapassar {ValorTotalMaximo:N2}.");

    internal static Error QuantidadeInvalida(string codigo) => Error.Validation(
        codigo,
        $"Quantidade deve estar entre 1 e {QuantidadeMaximaPorItem}.");
}
