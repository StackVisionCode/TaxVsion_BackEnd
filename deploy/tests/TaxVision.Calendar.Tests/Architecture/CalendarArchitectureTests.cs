using System.Reflection;
using NetArchTest.Rules;
using TaxVision.Calendar.Domain.Appointments;
using Xunit;

namespace TaxVision.Calendar.Tests.Architecture;

public sealed class CalendarArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Appointment).Assembly;

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is null ? "(sin detalle)" : string.Join(", ", result.FailingTypeNames);

    /// <summary>
    /// Si Domain referenciara EF, las decisiones del agregado quedarian imposibles de probar sin base
    /// de datos. <c>Ical.Net</c> si esta permitido: es una libreria de dominio puro, sin IO.
    /// </summary>
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("TaxVision.Calendar.Infrastructure")]
    [InlineData("TaxVision.Calendar.Application")]
    public void Domain_should_not_depend_on_infrastructure_concerns(string forbiddenNamespace)
    {
        var result = Types.InAssembly(DomainAssembly).ShouldNot().HaveDependencyOn(forbiddenNamespace).GetResult();

        Assert.True(result.IsSuccessful, $"Domain types depending on {forbiddenNamespace}: " + Describe(result));
    }

    /// <summary>
    /// Guardrail #6. Un <c>Any()</c> o un <c>Where()</c> dentro de un agregado esconde un recorrido
    /// que despues resulta ser O(n) sobre una coleccion cargada entera desde la BD. En el dominio del
    /// tiempo, ademas, el bucle explicito deja ver el orden de las operaciones — que es justo donde
    /// vive el bug de DST.
    /// </summary>
    [Fact]
    public void Domain_should_not_use_linq()
    {
        var result = Types.InAssembly(DomainAssembly).ShouldNot().HaveDependencyOn("System.Linq").GetResult();

        Assert.True(result.IsSuccessful, "Domain types using LINQ: " + Describe(result));
    }

    /// <summary>
    /// ADR-C-04: las ocurrencias se calculan al vuelo y no existe tabla que las guarde. Una serie de
    /// tres anios son 156 ocurrencias y <b>una</b> fila; materializarlas es la deuda del legacy
    /// volviendo, y ademas deja datos que quedan mal el dia que un pais cambia sus reglas de DST.
    ///
    /// <para>
    /// <c>Occurrence</c> existe como record de calculo y esta bien. Lo que la regla prohibe es que
    /// algo con «Occurrence» en el nombre sea una <b>entidad</b> — que es como empezaria una tabla.
    /// </para>
    /// </summary>
    [Fact]
    public void No_occurrence_type_is_an_entity()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .That()
            .HaveNameMatching(".*Occurrence.*")
            .Should()
            .NotBeClasses()
            .Or()
            .NotInherit(typeof(BuildingBlocks.Domain.BaseEntity))
            .GetResult();

        Assert.True(result.IsSuccessful, "Occurrence types modelled as entities: " + Describe(result));
    }
}
