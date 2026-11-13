using MediatR;
using OrdensServico.Adapters.Models.Request;
using OrdensServico.Adapters.Models.ViewModels;
using OrdensServico.Adapters.Presenters;
using OrdensServico.Application.Ordens.Commands.AbrirOrdemServicoCompleta;
using OrdensServico.Application.Ordens.Commands.AprovarOrcamento;
using OrdensServico.Application.Ordens.Commands.ConcluirOrdemServico;
using OrdensServico.Application.Ordens.Commands.FinalizarOrdemServico;
using OrdensServico.Application.Ordens.Commands.GerarOrdemServico;
using OrdensServico.Application.Ordens.Commands.IniciarDiagnostico;
using OrdensServico.Application.Ordens.Commands.RegistrarDiagnostico;
using OrdensServico.Application.Ordens.Commands.RejeitarOrcamento;
using OrdensServico.Application.Ordens.Queries.ListarOrdensParaAcompanhamento;
using OrdensServico.Application.Ordens.Queries.ListarOrdensPorCliente;
using OrdensServico.Application.Ordens.Queries.ObterOrdemServicoPorId;
using SharedKernel.Domain;

namespace OrdensServico.Adapters.Controllers;

public sealed class OrdemServicoController
{
    private readonly ISender _sender;

    public OrdemServicoController(ISender sender) => _sender = sender;

    public async Task<Result<OrdemServicoViewModel>> Gerar(GerarOrdemServicoRequest request)
    {
        var command = new GerarOrdemServicoCommand(request.ClienteId, request.VeiculoId);
        var result = await _sender.Send(command);
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.Present(result.Value);
    }

    public async Task<Result<OrdemServicoViewModel>> AbrirCompleta(AbrirOrdemServicoCompletaRequest request)
    {
        var command = new AbrirOrdemServicoCompletaCommand(request.ClienteId, request.VeiculoId, request.Servicos, request.Pecas);
        var result = await _sender.Send(command);
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.Present(result.Value);
    }

    public async Task<Result<OrdemServicoViewModel>> IniciarDiagnostico(Guid id)
    {
        var result = await _sender.Send(new IniciarDiagnosticoCommand(id));
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.Present(result.Value);
    }

    public async Task<Result<OrdemServicoViewModel>> RegistrarDiagnostico(Guid id, RegistrarDiagnosticoRequest request)
    {
        var command = new RegistrarDiagnosticoCommand(id, request.DescricaoDiagnostico, request.Servicos, request.Pecas);
        var result = await _sender.Send(command);
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.Present(result.Value);
    }

    public async Task<Result<OrdemServicoViewModel>> AprovarOrcamento(Guid id, Guid? solicitanteClienteId = null)
    {
        var result = await _sender.Send(new AprovarOrcamentoCommand(id, solicitanteClienteId));
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.Present(result.Value);
    }

    public async Task<Result<OrdemServicoViewModel>> RejeitarOrcamento(Guid id, Guid? solicitanteClienteId = null)
    {
        var result = await _sender.Send(new RejeitarOrcamentoCommand(id, solicitanteClienteId));
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.Present(result.Value);
    }

    public async Task<Result<OrdemServicoViewModel>> Finalizar(Guid id)
    {
        var result = await _sender.Send(new FinalizarOrdemServicoCommand(id));
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.Present(result.Value);
    }

    public async Task<Result<OrdemServicoViewModel>> Concluir(Guid id)
    {
        var result = await _sender.Send(new ConcluirOrdemServicoCommand(id));
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.Present(result.Value);
    }

    public async Task<Result<OrdemServicoViewModel>> ObterPorId(Guid id, Guid? solicitanteClienteId = null)
    {
        var result = await _sender.Send(new ObterOrdemServicoPorIdQuery(id, solicitanteClienteId));
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.Present(result.Value);
    }

    public async Task<Result<StatusOrdemServicoViewModel>> ObterStatus(Guid id, Guid? solicitanteClienteId = null)
    {
        var result = await _sender.Send(new ObterOrdemServicoPorIdQuery(id, solicitanteClienteId));
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.PresentStatus(result.Value);
    }

    public async Task<Result<List<OrdemServicoViewModel>>> ListarPorCliente(Guid clienteId)
    {
        var result = await _sender.Send(new ListarOrdensPorClienteQuery(clienteId));
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.PresentListar(result.Value);
    }

    public async Task<Result<List<OrdemServicoViewModel>>> ListarParaAcompanhamento(Guid clienteId)
    {
        var result = await _sender.Send(new ListarOrdensParaAcompanhamentoQuery(clienteId));
        if (result.IsFailure) return result.Error;
        return OrdemServicoPresenter.PresentListar(result.Value);
    }
}
