using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrdensServico.Adapters.Controllers;
using OrdensServico.Adapters.Models.Request;
using OrdensServico.Web.Extensions;

namespace OrdensServico.Web.Controllers;

// Mapa de papéis por rota — RFC-001 §4.2. [Authorize] na classe só exige usuário autenticado:
// múltiplos atributos [Authorize(Roles=...)] (classe + método) são combinados com E, não
// sobrescritos — por isso o papel é declarado explicitamente em cada ação.
[Authorize]
[ApiController]
[Route("api/v1/ordens-servico")]
public class OrdemServicoApiController : ControllerBase
{
    // Mantido em sincronia com OrdemServicoErrors.AcessoNegado.Code (Application é internal).
    private const string AcessoNegadoErrorCode = "OrdemServico.AcessoNegado";

    private readonly OrdemServicoController _caController;

    public OrdemServicoApiController(OrdemServicoController caController) => _caController = caController;

    [Authorize(Roles = "Oficina")]
    [HttpPost]
    public async Task<IActionResult> Gerar([FromBody] GerarOrdemServicoRequest request)
    {
        var result = await _caController.Gerar(request);

        if (result.IsFailure)
            return UnprocessableEntity(result.Error);

        return CreatedAtAction(nameof(ObterPorId), new { id = result.Value.Id }, result.Value);
    }

    [Authorize(Roles = "Oficina")]
    [HttpPost("completa")]
    public async Task<IActionResult> AbrirCompleta([FromBody] AbrirOrdemServicoCompletaRequest request)
    {
        var result = await _caController.AbrirCompleta(request);

        if (result.IsFailure)
            return UnprocessableEntity(result.Error);

        return CreatedAtAction(nameof(ObterPorId), new { id = result.Value.Id }, result.Value);
    }

    [Authorize(Roles = "Cliente,Oficina")]
    [HttpGet("{id}")]
    public async Task<IActionResult> ObterPorId(Guid id)
    {
        var result = await _caController.ObterPorId(id, User.ObterClienteIdDoSolicitante());

        if (result.IsFailure)
            return result.Error.Code == AcessoNegadoErrorCode
                ? StatusCode(StatusCodes.Status403Forbidden, result.Error)
                : NotFound(result.Error);

        return Ok(result.Value);
    }

    [Authorize(Roles = "Cliente,Oficina")]
    [HttpGet("{id}/status")]
    public async Task<IActionResult> ObterStatus(Guid id)
    {
        var result = await _caController.ObterStatus(id, User.ObterClienteIdDoSolicitante());

        if (result.IsFailure)
            return result.Error.Code == AcessoNegadoErrorCode
                ? StatusCode(StatusCodes.Status403Forbidden, result.Error)
                : NotFound(result.Error);

        return Ok(result.Value);
    }

    // Listagem completa por cliente — ver RFC-001 §7 "Questões em aberto": era
    // AllowAnonymous, decisão desta fase foi protegê-la com papel Oficina.
    [Authorize(Roles = "Oficina")]
    [HttpGet]
    public async Task<IActionResult> ListarPorCliente([FromQuery] Guid clienteId)
    {
        var result = await _caController.ListarPorCliente(clienteId);

        if (result.IsFailure)
            return NotFound(result.Error);

        return Ok(result.Value);
    }

    [Authorize(Roles = "Cliente")]
    [HttpGet("acompanhamento")]
    public async Task<IActionResult> ListarParaAcompanhamento()
    {
        var clienteId = User.ObterClienteIdDoSolicitante() ?? Guid.Empty;
        var result = await _caController.ListarParaAcompanhamento(clienteId);

        if (result.IsFailure)
            return NotFound(result.Error);

        return Ok(result.Value);
    }

    [Authorize(Roles = "Oficina")]
    [HttpPatch("{id}/iniciar-diagnostico")]
    public async Task<IActionResult> IniciarDiagnostico(Guid id)
    {
        var result = await _caController.IniciarDiagnostico(id);

        if (result.IsFailure)
            return UnprocessableEntity(result.Error);

        return Ok(result.Value);
    }

    [Authorize(Roles = "Oficina")]
    [HttpPatch("{id}/registrar-diagnostico")]
    public async Task<IActionResult> RegistrarDiagnostico(Guid id, [FromBody] RegistrarDiagnosticoRequest request)
    {
        var result = await _caController.RegistrarDiagnostico(id, request);

        if (result.IsFailure)
            return UnprocessableEntity(result.Error);

        return Ok(result.Value);
    }

    [Authorize(Roles = "Cliente")]
    [HttpPatch("{id}/aprovar-orcamento")]
    public async Task<IActionResult> AprovarOrcamento(Guid id)
    {
        var result = await _caController.AprovarOrcamento(id, User.ObterClienteIdDoSolicitante());

        if (result.IsFailure)
            return result.Error.Code == AcessoNegadoErrorCode
                ? StatusCode(StatusCodes.Status403Forbidden, result.Error)
                : UnprocessableEntity(result.Error);

        return Ok(result.Value);
    }

    [Authorize(Roles = "Cliente")]
    [HttpPatch("{id}/rejeitar-orcamento")]
    public async Task<IActionResult> RejeitarOrcamento(Guid id)
    {
        var result = await _caController.RejeitarOrcamento(id, User.ObterClienteIdDoSolicitante());

        if (result.IsFailure)
            return result.Error.Code == AcessoNegadoErrorCode
                ? StatusCode(StatusCodes.Status403Forbidden, result.Error)
                : UnprocessableEntity(result.Error);

        return Ok(result.Value);
    }

    [Authorize(Roles = "Oficina")]
    [HttpPatch("{id}/finalizar")]
    public async Task<IActionResult> Finalizar(Guid id)
    {
        var result = await _caController.Finalizar(id);

        if (result.IsFailure)
            return UnprocessableEntity(result.Error);

        return Ok(result.Value);
    }

    [Authorize(Roles = "Oficina")]
    [HttpPatch("{id}/concluir")]
    public async Task<IActionResult> Concluir(Guid id)
    {
        var result = await _caController.Concluir(id);

        if (result.IsFailure)
            return UnprocessableEntity(result.Error);

        return Ok(result.Value);
    }
}
