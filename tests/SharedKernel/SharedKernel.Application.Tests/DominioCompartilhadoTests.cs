using SharedKernel.Domain;

namespace SharedKernel.Application.Tests;

public class DominioCompartilhadoTests
{
    private sealed class Pessoa(Guid id) : Entity<Guid>(id);
    private sealed class Outra(Guid id) : Entity<Guid>(id);

    private sealed class Dinheiro(decimal valor, string? moeda) : ValueObject
    {
        protected override IEnumerable<object?> GetComponents()
        {
            yield return valor;
            yield return moeda;
        }
    }

    [Fact(DisplayName = "Entity: mesma identidade e tipo → iguais, com mesmo hash")]
    public void Entity_MesmoIdETipo_SaoIguais()
    {
        var id = Guid.NewGuid();
        var a = new Pessoa(id);
        var b = new Pessoa(id);

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.Equals(a));
    }

    [Fact(DisplayName = "Entity: id diferente, tipo diferente ou null → diferentes")]
    public void Entity_IdOuTipoDiferente_SaoDiferentes()
    {
        var id = Guid.NewGuid();
        var a = new Pessoa(id);

        Assert.False(a == new Pessoa(Guid.NewGuid()));
        Assert.False(a.Equals(new Outra(id)));
        Assert.False(a.Equals("texto"));
        Assert.False(a == null);
        Assert.True((Pessoa?)null == (Pessoa?)null);
    }

    [Fact(DisplayName = "ValueObject: mesmos componentes → iguais, com mesmo hash")]
    public void ValueObject_MesmosComponentes_SaoIguais()
    {
        var a = new Dinheiro(10m, "BRL");
        var b = new Dinheiro(10m, "BRL");

        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact(DisplayName = "ValueObject: componentes diferentes, tipo diferente ou null → diferentes")]
    public void ValueObject_ComponentesDiferentes_SaoDiferentes()
    {
        var a = new Dinheiro(10m, "BRL");

        Assert.True(a != new Dinheiro(11m, "BRL"));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals("texto"));
        Assert.True(a != null);
        Assert.True((Dinheiro?)null == (Dinheiro?)null);
        Assert.Equal(new Dinheiro(1m, null).GetHashCode(), new Dinheiro(1m, null).GetHashCode());
    }

    [Fact(DisplayName = "Result: fábricas estáticas e conversões implícitas")]
    public void Result_FabricasEConversoes()
    {
        var ok = Result.Success(5);
        var erro = Error.Conflict("x.conflito", "Conflito.");
        var falha = Result.Failure<int>(erro);
        Result<int> implicitoOk = 9;
        Result<int> implicitoErro = erro;

        Assert.True(ok.IsSuccess);
        Assert.Equal(Error.None, ok.Error);
        Assert.Equal(5, ok.Value);
        Assert.True(falha.IsFailure);
        Assert.Equal(erro, falha.Error);
        Assert.Throws<InvalidOperationException>(() => falha.Value);
        Assert.Equal(9, implicitoOk.Value);
        Assert.Equal(erro, implicitoErro.Error);
    }
}
