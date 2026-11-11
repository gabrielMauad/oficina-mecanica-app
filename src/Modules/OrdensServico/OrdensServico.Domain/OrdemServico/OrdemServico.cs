using OrdensServico.Domain.OrdemServico.Events;
using SharedKernel.Domain;

namespace OrdensServico.Domain.OrdemServico;

public sealed class OrdemServico : AggregateRoot<OrdemServicoId>
{
    private readonly List<ItemPeca> _itensPeca = [];
    private readonly List<ItemServico> _itensServico = [];
    private readonly List<Orcamento> _orcamentos = [];

    public Guid ClienteId { get; private set; }
    public Guid VeiculoId { get; private set; }
    public StatusOrdemServico Status { get; private set; }
    public string? DescricaoDiagnostico { get; private set; }
    public DateTime? NotificadoEm { get; private set; }
    public DateTime? EntregueEm { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }
    public DateTime StatusAlteradoEm { get; private set; }

    public IReadOnlyList<ItemPeca> ItensPeca => _itensPeca.AsReadOnly();
    public IReadOnlyList<ItemServico> ItensServico => _itensServico.AsReadOnly();
    public IReadOnlyList<Orcamento> Orcamentos => _orcamentos.AsReadOnly();

    private OrdemServico(
        OrdemServicoId id,
        Guid clienteId,
        Guid veiculoId
    ) : base(id)
    {
        ClienteId = clienteId;
        VeiculoId = veiculoId;
        Status = StatusOrdemServico.Recebida;
        CriadoEm = DateTime.UtcNow;
        AtualizadoEm = DateTime.UtcNow;
        StatusAlteradoEm = DateTime.UtcNow;
    }

    private OrdemServico(
        OrdemServicoId id,
        Guid clienteId,
        Guid veiculoId,
        StatusOrdemServico status,
        string? descricaoDiagnostico,
        DateTime? notificadoEm,
        DateTime? entregueEm,
        DateTime criadoEm,
        DateTime atualizadoEm,
        DateTime statusAlteradoEm,
        IEnumerable<ItemServico> itensServico,
        IEnumerable<ItemPeca> itensPeca,
        IEnumerable<Orcamento> orcamentos
    ) : base(id)
    {
        ClienteId = clienteId;
        VeiculoId = veiculoId;
        Status = status;
        DescricaoDiagnostico = descricaoDiagnostico;
        NotificadoEm = notificadoEm;
        EntregueEm = entregueEm;
        CriadoEm = criadoEm;
        AtualizadoEm = atualizadoEm;
        StatusAlteradoEm = statusAlteradoEm;
        _itensServico.AddRange(itensServico);
        _itensPeca.AddRange(itensPeca);
        _orcamentos.AddRange(orcamentos);
    }

    public static OrdemServico Reconstituir(
        OrdemServicoId id,
        Guid clienteId,
        Guid veiculoId,
        StatusOrdemServico status,
        string? descricaoDiagnostico,
        DateTime? notificadoEm,
        DateTime? entregueEm,
        DateTime criadoEm,
        DateTime atualizadoEm,
        DateTime statusAlteradoEm,
        IEnumerable<ItemServico> itensServico,
        IEnumerable<ItemPeca> itensPeca,
        IEnumerable<Orcamento> orcamentos) =>
        new(id, clienteId, veiculoId, status, descricaoDiagnostico, notificadoEm, entregueEm,
            criadoEm, atualizadoEm, statusAlteradoEm, itensServico, itensPeca, orcamentos);

    public static Result<OrdemServico> Criar(Guid clienteId, Guid veiculoId)
    {
        if (clienteId == Guid.Empty)
            return Error.Validation("OrdemServico.ClienteIdVazio", "ClienteId é obrigatório.");
        if (veiculoId == Guid.Empty)
            return Error.Validation("OrdemServico.VeiculoIdVazio", "VeiculoId é obrigatório.");

        var ordemServico = new OrdemServico(OrdemServicoId.Novo(), clienteId, veiculoId);
        return ordemServico;
    }

    public static Result<OrdemServico> AbrirComServicos(
        Guid clienteId,
        Guid veiculoId,
        IEnumerable<ItemServicoInput> servicos,
        IEnumerable<ItemPecaInput> pecas)
    {
        if (clienteId == Guid.Empty)
            return Error.Validation("OrdemServico.ClienteIdVazio", "ClienteId é obrigatório.");
        if (veiculoId == Guid.Empty)
            return Error.Validation("OrdemServico.VeiculoIdVazio", "VeiculoId é obrigatório.");

        var os = new OrdemServico(OrdemServicoId.Novo(), clienteId, veiculoId);
        var resultado = os.MontarOrcamentoEEmitirEvento(descricaoDiagnostico: null, servicos, pecas);
        if (resultado.IsFailure) return resultado.Error;

        os.Status = StatusOrdemServico.AguardandoAprovacao;
        os.StatusAlteradoEm = DateTime.UtcNow;
        return os;
    }

    public Result<OrdemServico> IniciarDiagnostico()
    {
        if (Status != StatusOrdemServico.Recebida)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode entrar em diagnóstico quando está Recebida.");

        Status = StatusOrdemServico.EmDiagnostico;
        AtualizadoEm = DateTime.UtcNow;
        StatusAlteradoEm = DateTime.UtcNow;
        return this;
    }

    public Result<OrdemServico> RegistrarDiagnostico(
        string descricaoDiagnostico,
        IEnumerable<ItemServicoInput> servicos,
        IEnumerable<ItemPecaInput> pecas)
    {
        if (Status != StatusOrdemServico.EmDiagnostico && Status != StatusOrdemServico.AguardandoAprovacao)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode registrar diagnóstico quando está Em Diagnóstico ou Aguardando Aprovação.");
        if (_orcamentos.Any(x => x.Status != StatusOrcamento.Rejeitado))
            return Error.Validation("OrdemServico.OrcamentoExistente", "Já existe um orçamento ativo para esta ordem de serviço.");
        if (string.IsNullOrWhiteSpace(descricaoDiagnostico))
            return Error.Validation("OrdemServico.DiagnosticoVazio", "Descrição do diagnóstico é obrigatória.");

        var resultado = MontarOrcamentoEEmitirEvento(descricaoDiagnostico, servicos, pecas);
        if (resultado.IsFailure) return resultado.Error;

        DescricaoDiagnostico = descricaoDiagnostico;
        return this;
    }

    private Result<OrdemServico> MontarOrcamentoEEmitirEvento(
        string? descricaoDiagnostico,
        IEnumerable<ItemServicoInput> servicos,
        IEnumerable<ItemPecaInput> pecas)
    {
        var servicosList = servicos.ToList();
        var pecasList = pecas.ToList();

        if (servicosList.Count == 0)
            return Error.Validation("OrdemServico.OrcamentoSemServicos", "Não é possível gerar orçamento sem itens de serviços.");
        if (pecasList.Count == 0)
            return Error.Validation("OrdemServico.OrcamentoSemPecas", "Não é possível gerar orçamento sem itens de peças.");

        _itensServico.Clear();
        _itensPeca.Clear();

        foreach (var input in servicosList)
        {
            var itemResult = ItemServico.Criar(input.ServicoId, input.Quantidade, input.PrecoUnitario);
            if (itemResult.IsFailure) return itemResult.Error;
            _itensServico.Add(itemResult.Value);
        }

        foreach (var input in pecasList)
        {
            var itemResult = ItemPeca.Criar(input.PecaInsumoId, input.Quantidade, input.PrecoUnitario);
            if (itemResult.IsFailure) return itemResult.Error;
            _itensPeca.Add(itemResult.Value);
        }

        var valorTotal = _itensPeca.Sum(x => x.Quantidade * x.PrecoUnitarioSnapshot)
                       + _itensServico.Sum(x => x.Quantidade * x.PrecoUnitarioSnapshot);

        var orcamento = Orcamento.Criar(valorTotal);
        _orcamentos.Add(orcamento);

        AtualizadoEm = DateTime.UtcNow;

        var servicosSnapshot = _itensServico
            .Select(x => new ItemServicoSnapshot(x.ServicoId, x.Quantidade, x.PrecoUnitarioSnapshot))
            .ToList();
        var pecasSnapshot = _itensPeca
            .Select(x => new ItemPecaSnapshot(x.PecaInsumoId, x.Quantidade, x.PrecoUnitarioSnapshot))
            .ToList();

        AddDomainEvent(new OrcamentoGerado(Id, orcamento.Id, descricaoDiagnostico, servicosSnapshot, pecasSnapshot, valorTotal, DateTime.UtcNow));
        return this;
    }

    public Result<OrdemServico> EnviarOrcamento(DateTime dataEnvio)
    {
        if (Status != StatusOrdemServico.EmDiagnostico && Status != StatusOrdemServico.AguardandoAprovacao)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode enviar orçamento quando está Em Diagnóstico ou Aguardando Aprovação.");

        var orcamento = _orcamentos.FirstOrDefault(x => x.Status == StatusOrcamento.Pendente);
        if (orcamento == null)
            return Error.Validation("OrdemServico.OrcamentoNaoEncontrado", "Nenhum orçamento pendente encontrado para esta ordem de serviço.");

        var resultado = orcamento.Enviar(dataEnvio);
        if (resultado.IsFailure) return resultado.Error;

        Status = StatusOrdemServico.AguardandoAprovacao;
        AtualizadoEm = DateTime.UtcNow;
        StatusAlteradoEm = DateTime.UtcNow;
        return this;
    }

    public Result<OrdemServico> AprovarOrcamento()
    {
        if (Status != StatusOrdemServico.AguardandoAprovacao)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode aprovar orçamento quando está Aguardando Aprovação.");

        var orcamento = _orcamentos.FirstOrDefault(x => x.Status == StatusOrcamento.Enviado);
        if (orcamento == null)
            return Error.Validation("OrdemServico.OrcamentoNaoEncontrado", "Nenhum orçamento enviado encontrado para esta ordem de serviço.");

        var resultado = orcamento.Aprovar();
        if (resultado.IsFailure)
            return resultado.Error;

        AtualizadoEm = DateTime.UtcNow;
        AddDomainEvent(new OrcamentoAprovado(Id, orcamento.Id, ClienteId, orcamento.ValorTotal, DateTime.UtcNow));
        return this;
    }

    public Result<OrdemServico> RejeitarOrcamento()
    {
        if (Status != StatusOrdemServico.AguardandoAprovacao)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode rejeitar orçamento quando está Aguardando Aprovação.");

        var orcamento = _orcamentos.FirstOrDefault(x => x.Status == StatusOrcamento.Enviado);
        if (orcamento == null)
            return Error.Validation("OrdemServico.OrcamentoNaoEncontrado", "Nenhum orçamento enviado encontrado para esta ordem de serviço.");

        var resultado = orcamento.Rejeitar();
        if (resultado.IsFailure)
            return resultado.Error;

        var pecasSnapshot = _itensPeca
            .Select(x => new ItemPecaSnapshot(x.PecaInsumoId, x.Quantidade, x.PrecoUnitarioSnapshot))
            .ToList();

        AtualizadoEm = DateTime.UtcNow;
        AddDomainEvent(new OrcamentoRejeitado(Id, orcamento.Id, pecasSnapshot, DateTime.UtcNow));
        return this;
    }

    /// <summary>
    /// Compensação do pagamento recusado: o orçamento Aprovado passa a Rejeitado e o evento
    /// <see cref="OrcamentoRejeitado"/> leva as peças de volta ao estoque. A OS continua em
    /// Aguardando Aprovação, apta a receber um novo diagnóstico.
    /// </summary>
    public Result<OrdemServico> RejeitarOrcamentoAprovadoPorPagamentoRecusado(OrcamentoId orcamentoId)
    {
        if (Status != StatusOrdemServico.AguardandoAprovacao)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode desfazer a aprovação do orçamento quando está Aguardando Aprovação.");

        var orcamento = _orcamentos.FirstOrDefault(x => x.Id == orcamentoId);
        if (orcamento == null)
            return Error.Validation("OrdemServico.OrcamentoNaoEncontrado", "Orçamento não encontrado nesta ordem de serviço.");

        var resultado = orcamento.RejeitarAposAprovacao();
        if (resultado.IsFailure)
            return resultado.Error;

        var pecasSnapshot = _itensPeca
            .Select(x => new ItemPecaSnapshot(x.PecaInsumoId, x.Quantidade, x.PrecoUnitarioSnapshot))
            .ToList();

        AtualizadoEm = DateTime.UtcNow;
        AddDomainEvent(new OrcamentoRejeitado(Id, orcamento.Id, pecasSnapshot, DateTime.UtcNow));
        return this;
    }

    /// <summary>
    /// Falha na baixa de estoque: o orçamento passa a Rejeitado sem registrar
    /// <see cref="OrcamentoRejeitado"/>, pois nada foi baixado e não há o que estornar.
    /// </summary>
    public Result<OrdemServico> RejeitarOrcamentoPorFalhaNaBaixaDeEstoque(OrcamentoId orcamentoId)
    {
        if (Status != StatusOrdemServico.AguardandoAprovacao)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode rejeitar o orçamento por falha de estoque quando está Aguardando Aprovação.");

        var orcamento = _orcamentos.FirstOrDefault(x => x.Id == orcamentoId);
        if (orcamento == null)
            return Error.Validation("OrdemServico.OrcamentoNaoEncontrado", "Orçamento não encontrado nesta ordem de serviço.");

        var resultado = orcamento.RejeitarPorFalhaDeEstoque();
        if (resultado.IsFailure)
            return resultado.Error;

        AtualizadoEm = DateTime.UtcNow;
        return this;
    }

    public Result<OrdemServico> Executar()
    {
        if (Status != StatusOrdemServico.AguardandoAprovacao)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode iniciar execução quando está Aguardando Aprovação.");
        if (!_orcamentos.Any(x => x.Status == StatusOrcamento.Aprovado))
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode iniciar execução quando o orçamento está Aprovado.");

        Status = StatusOrdemServico.EmExecucao;
        AtualizadoEm = DateTime.UtcNow;
        StatusAlteradoEm = DateTime.UtcNow;
        return this;
    }

    public Result<OrdemServico> Finalizar()
    {
        if (Status != StatusOrdemServico.EmExecucao)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode finalizar quando está Em Execução.");

        Status = StatusOrdemServico.Finalizada;
        AtualizadoEm = DateTime.UtcNow;
        StatusAlteradoEm = DateTime.UtcNow;
        AddDomainEvent(new OrdemServicoFinalizada(Id, ClienteId, DateTime.UtcNow));
        return this;
    }

    public Result<OrdemServico> NotificarCliente(DateTime dataNotificacao)
    {
        if (Status != StatusOrdemServico.Finalizada)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode notificar cliente quando está Finalizada.");

        NotificadoEm = dataNotificacao;
        AtualizadoEm = DateTime.UtcNow;
        return this;
    }

    public Result<OrdemServico> Concluir(DateTime dataEntrega)
    {
        if (Status != StatusOrdemServico.Finalizada || NotificadoEm == null)
            return Error.Validation("OrdemServico.TransicaoInvalida", "Ordem de Serviço só pode ser concluída quando está Finalizada e o cliente foi notificado.");

        EntregueEm = dataEntrega;
        Status = StatusOrdemServico.Entregue;
        AtualizadoEm = DateTime.UtcNow;
        StatusAlteradoEm = DateTime.UtcNow;
        return this;
    }
}
