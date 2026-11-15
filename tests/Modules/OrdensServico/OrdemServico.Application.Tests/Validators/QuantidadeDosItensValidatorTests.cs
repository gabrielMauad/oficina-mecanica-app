using OrdensServico.Application.Ordens.Commands.AbrirOrdemServicoCompleta;
using OrdensServico.Application.Ordens.Commands.RegistrarDiagnostico;
using OrdensServico.Domain.OrdemServico;

namespace OrdensServico.Application.Tests.Validators;

public class QuantidadeDosItensValidatorTests
{
    private static readonly Guid Id = Guid.NewGuid();

    private static AbrirOrdemServicoCompletaCommand Abrir(int quantidadeServico, int quantidadePeca) => new(
        Id, Id, [new ServicoItem(Id, quantidadeServico)], [new PecaItem(Id, quantidadePeca)]);

    private static RegistrarDiagnosticoCommand Diagnosticar(int quantidadeServico, int quantidadePeca) => new(
        Id, "descrição", [new ServicoItem(Id, quantidadeServico)], [new PecaItem(Id, quantidadePeca)]);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(LimitesDoOrcamento.QuantidadeMaximaPorItem, LimitesDoOrcamento.QuantidadeMaximaPorItem)]
    public void Abrir_QuantidadesNaFaixa_SaoValidas(int servico, int peca) =>
        Assert.True(new AbrirOrdemServicoCompletaValidator().Validate(Abrir(servico, peca)).IsValid);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(LimitesDoOrcamento.QuantidadeMaximaPorItem + 1, 1)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(1, 0)]
    [InlineData(1, LimitesDoOrcamento.QuantidadeMaximaPorItem + 1)]
    [InlineData(1, int.MaxValue)]
    public void Abrir_QuantidadesForaDaFaixa_SaoInvalidas(int servico, int peca)
    {
        var resultado = new AbrirOrdemServicoCompletaValidator().Validate(Abrir(servico, peca));

        Assert.False(resultado.IsValid);
        Assert.Contains(resultado.Errors, e => e.ErrorMessage.Contains("entre 1 e 999"));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(LimitesDoOrcamento.QuantidadeMaximaPorItem, LimitesDoOrcamento.QuantidadeMaximaPorItem)]
    public void Diagnosticar_QuantidadesNaFaixa_SaoValidas(int servico, int peca) =>
        Assert.True(new RegistrarDiagnosticoValidator().Validate(Diagnosticar(servico, peca)).IsValid);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(1, 0)]
    [InlineData(1, int.MaxValue)]
    public void Diagnosticar_QuantidadesForaDaFaixa_SaoInvalidas(int servico, int peca)
    {
        var resultado = new RegistrarDiagnosticoValidator().Validate(Diagnosticar(servico, peca));

        Assert.False(resultado.IsValid);
        Assert.Contains(resultado.Errors, e => e.ErrorMessage.Contains("entre 1 e 999"));
    }
}
