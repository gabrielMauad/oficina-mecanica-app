using FluentValidation;
using MediatR;
using SharedKernel.Application.Behaviors;
using SharedKernel.Domain;

namespace SharedKernel.Application.Tests;

public class ValidationBehaviorTests
{
    private sealed record Pedido(string Nome);

    private sealed class NomeObrigatorio : AbstractValidator<Pedido>
    {
        public NomeObrigatorio() => RuleFor(p => p.Nome).NotEmpty().WithMessage("Nome é obrigatório.");
    }

    private sealed class NomeCurto : AbstractValidator<Pedido>
    {
        public NomeCurto() => RuleFor(p => p.Nome).MaximumLength(3).WithMessage("Nome muito longo.");
    }

    private static Task<TResponse> Executar<TResponse>(
        Pedido pedido, TResponse respostaDoProximo, out Func<int> chamadas, params IValidator<Pedido>[] validators)
        where TResponse : notnull
    {
        var contador = 0;
        chamadas = () => contador;
        var behavior = new ValidationBehavior<Pedido, TResponse>(validators);
        return behavior.Handle(pedido, _ => { contador++; return Task.FromResult(respostaDoProximo); }, CancellationToken.None);
    }

    [Fact(DisplayName = "Sem validators → segue para o próximo handler")]
    public async Task SemValidators_ChamaProximo()
    {
        var resposta = await Executar(new Pedido(""), Result.Success(1), out var chamadas);

        Assert.True(resposta.IsSuccess);
        Assert.Equal(1, chamadas());
    }

    [Fact(DisplayName = "Request válido → segue para o próximo handler")]
    public async Task RequestValido_ChamaProximo()
    {
        var resposta = await Executar(new Pedido("ok"), Result.Success(7), out var chamadas, new NomeObrigatorio());

        Assert.Equal(7, resposta.Value);
        Assert.Equal(1, chamadas());
    }

    [Fact(DisplayName = "Request inválido com Result<T> → falha de validação agregando as mensagens, sem chamar o handler")]
    public async Task RequestInvalido_RetornaFalhaComMensagensAgregadas()
    {
        var resposta = await Executar(
            new Pedido(""),
            Result.Success(1), out var chamadas, new NomeObrigatorio(), new NomeCurto());

        Assert.True(resposta.IsFailure);
        Assert.Equal("validation.failed", resposta.Error.Code);
        Assert.Contains("Nome é obrigatório.", resposta.Error.Message);
        Assert.Equal(0, chamadas());
    }

    [Fact(DisplayName = "Request que viola apenas um de vários validators → mensagem só desse validator")]
    public async Task ViolaApenasUmValidator_MensagemDele()
    {
        var resposta = await Executar(
            new Pedido("abcd"), Result.Success(1), out _, new NomeCurto(), new NomeObrigatorio());

        Assert.Contains("Nome muito longo.", resposta.Error.Message);
        Assert.DoesNotContain("obrigatório", resposta.Error.Message);
    }

    [Fact(DisplayName = "Request inválido com resposta que não é Result<T> → não bloqueia e chama o próximo")]
    public async Task RespostaQueNaoEResult_ChamaProximo()
    {
        var resposta = await Executar(new Pedido(""), "resposta", out var chamadas, new NomeObrigatorio());

        Assert.Equal("resposta", resposta);
        Assert.Equal(1, chamadas());
    }

    [Fact(DisplayName = "Dois validators e apenas um falha → a mensagem aparece uma única vez")]
    public async Task DoisValidators_UmFalha_MensagemApareceUmaVez()
    {
        var resposta = await Executar(
            new Pedido(""), Result.Success(1), out _, new NomeObrigatorio(), new NomeCurto());

        Assert.Equal("Nome é obrigatório.", resposta.Error.Message);

        var invertida = await Executar(
            new Pedido(""), Result.Success(1), out _, new NomeCurto(), new NomeObrigatorio());

        Assert.Equal("Nome é obrigatório.", invertida.Error.Message);
    }
}
