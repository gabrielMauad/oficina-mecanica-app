using SharedKernel.Domain;

namespace OrdensServico.Domain.OrdemServico;

public sealed class ItemServico : Entity<ItemServicoId>
{
    public Guid ServicoId { get; private set; }
    public int Quantidade { get; private set; }
    public decimal PrecoUnitarioSnapshot { get; private set; }

    private ItemServico(ItemServicoId id, Guid servicoId, int quantidade, decimal precoUnitarioSnapshot)
        : base(id)
    {
        ServicoId = servicoId;
        Quantidade = quantidade;
        PrecoUnitarioSnapshot = precoUnitarioSnapshot;
    }

    public static ItemServico Reconstituir(ItemServicoId id, Guid servicoId, int quantidade, decimal precoUnitarioSnapshot) =>
        new(id, servicoId, quantidade, precoUnitarioSnapshot);

    internal static Result<ItemServico> Criar(Guid servicoId, int quantidade, decimal precoUnitarioSnapshot)
    {
        if (servicoId == Guid.Empty)
            return Error.Validation("ItemServico.ServicoIdVazio", "ServicoId é obrigatório.");
        if (quantidade is <= 0 or > LimitesDoOrcamento.QuantidadeMaximaPorItem)
            return LimitesDoOrcamento.QuantidadeInvalida("ItemServico.QuantidadeInvalida");
        if (precoUnitarioSnapshot < 0)
            return Error.Validation("ItemServico.PrecoUnitarioSnapshotInvalido", "Preço unitário snapshot não pode ser negativo.");

        var itemServico = new ItemServico(ItemServicoId.Novo(), servicoId, quantidade, precoUnitarioSnapshot);
        return itemServico;
    }

    internal Result<ItemServico> AtualizarQuantidade(int novaQuantidade)
    {
        if (novaQuantidade is <= 0 or > LimitesDoOrcamento.QuantidadeMaximaPorItem)
            return LimitesDoOrcamento.QuantidadeInvalida("ItemServico.QuantidadeInvalida");

        Quantidade = novaQuantidade;
        return this;
    }

    internal Result<ItemServico> AtualizarPrecoUnitarioSnapshot(decimal novoPrecoUnitario)
    {
        if (novoPrecoUnitario < 0)
            return Error.Validation("ItemServico.PrecoUnitarioInvalido", "Preço unitário não pode ser negativo.");
        PrecoUnitarioSnapshot = novoPrecoUnitario;
        return this;
    }
}
