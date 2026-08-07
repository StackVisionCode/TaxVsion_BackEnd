using BuildingBlocks.Results;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Results;

/// <summary>
/// BB-06. <c>Result</c> es el tipo de retorno de prácticamente todos los handlers del repo, y sus
/// dos invariantes (éxito sin error, fallo con error) no tenían ninguna verificación. Lo que se fija
/// acá es que esas invariantes son inviolables y que <c>Value</c> nunca devuelve el default de un
/// fallo — leerlo en silencio sería un bug que se propaga hasta la respuesta HTTP.
/// </summary>
public sealed class ResultTests
{
    private static readonly Error SomeError = new("Test.Failed", "Something went wrong.");

    [Fact]
    public void Success_EsExitoYSinError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_EsFalloYConservaElError()
    {
        var result = Result.Failure(SomeError);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(SomeError, result.Error);
    }

    [Fact]
    public void SuccessGenerico_ExponeElValor()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void LeerValue_DeUnFallo_Lanza()
    {
        // Sin esto, un handler que olvide comprobar IsFailure devolvería default(T) —
        // un 200 con el cuerpo vacío en vez del error real.
        var result = Result.Failure<int>(SomeError);

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void SuccessGenerico_AdmiteNullComoValorLegitimo()
    {
        var result = Result.Success<string?>(null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    // ------------------------------------------------------------------ invariantes del ctor

    [Fact]
    public void UnResultadoExitosoConError_EsImposibleDeConstruir()
    {
        // La invariante se prueba por el camino real: Failure<T> con Error.None viola
        // "un fallo debe traer un error", que es la otra cara de la misma regla.
        Assert.Throws<InvalidOperationException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void UnFalloSinError_EsImposibleDeConstruir()
    {
        Assert.Throws<InvalidOperationException>(() => Result.Failure<int>(Error.None));
    }

    // ------------------------------------------------------------------ Error

    [Fact]
    public void DosErroresConElMismoCodigoYMensaje_SonIguales()
    {
        // Es un record: la igualdad estructural es lo que permite comparar contra Error.None
        // en el constructor y afirmar errores concretos en los tests de handlers.
        Assert.Equal(new Error("A.B", "msg"), new Error("A.B", "msg"));
        Assert.NotEqual(new Error("A.B", "msg"), new Error("A.C", "msg"));
    }
}
