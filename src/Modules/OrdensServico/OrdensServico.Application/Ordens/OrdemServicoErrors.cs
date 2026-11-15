using SharedKernel.Domain;

namespace OrdensServico.Application.Ordens;

internal class OrdemServicoErrors
{
    public static readonly Error ClienteInexistenteOuInativo = Error.Conflict(
        "OrdemServico.ClienteInexistenteOuInativo",
        "O cliente informado não existe ou está inativo.");

    public static readonly Error VeiculoInexistenteOuNaoPertenceAoCliente = Error.Conflict(
        "OrdemServico.VeiculoInexistenteOuNaoPertenceAoCliente",
        "O veículo informado não existe ou não pertence ao cliente.");

    public static readonly Error NaoEncontrada = Error.NotFound(
        "OrdemServico.NaoEncontrada",
        "Ordem de serviço não encontrada.");

    public static readonly Error AcessoNegado = Error.Forbidden(
        "OrdemServico.AcessoNegado",
        "Você não tem permissão para acessar esta ordem de serviço.");

    public static readonly Error ServicoNaoEncontrado = Error.Validation(
        "OrdemServico.ServicoNaoEncontrado",
        "Servico não encontrado.");

    public static readonly Error ServicoDesativado = Error.Validation(
        "OrdemServico.ServicoDesativado",
        "O serviço informado está desativado.");

    public static readonly Error PecaNaoEncontrada = Error.Validation(
        "OrdemServico.PecaNaoEncontrada",
        "Peça/insumo não encontrada.");

    public static readonly Error PecaIndisponivel = Error.Validation(
        "OrdemServico.PecaIndisponivel",
        "Peça/insumo indisponível para a quantidade informada.");
}
