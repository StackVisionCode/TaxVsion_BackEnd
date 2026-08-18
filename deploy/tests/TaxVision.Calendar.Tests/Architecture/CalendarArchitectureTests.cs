using System.Reflection;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using TaxVision.Calendar.Api.Controllers;
using TaxVision.Calendar.Application.Meetings.Consumers;
using TaxVision.Calendar.Domain.Appointments;
using Xunit;

namespace TaxVision.Calendar.Tests.Architecture;

public sealed class CalendarArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Appointment).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(MeetingLinkedConsumer).Assembly;
    private static readonly Assembly ApiAssembly = typeof(CalendarFeedController).Assembly;

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

    /// <summary>
    /// La sala es de Communication y el compromiso es de Calendar. Un solo consumer conoce ese
    /// contrato; en cuanto lo conozca un segundo tipo, la frontera se disuelve sin que nadie decida
    /// disolverla.
    /// </summary>
    [Fact]
    public void Only_the_meeting_consumer_knows_about_Communication()
    {
        var offenders = new List<string>();
        foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly, ApiAssembly })
        {
            var result = Types
                .InAssembly(assembly)
                .That()
                .DoNotHaveName(nameof(MeetingLinkedConsumer))
                .ShouldNot()
                .HaveDependencyOn("BuildingBlocks.Messaging.CommunicationIntegrationEvents")
                .GetResult();

            if (!result.IsSuccessful)
                offenders.AddRange(result.FailingTypeNames ?? []);
        }

        Assert.True(offenders.Count == 0, "Types coupled to Communication: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Un controller que alcanza Infrastructure se salta la capa que decide, y el dia que haya dos
    /// entradas al mismo caso de uso una de las dos no valida lo mismo.
    /// </summary>
    [Fact]
    public void Controllers_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .ShouldNot()
            .HaveDependencyOn("TaxVision.Calendar.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, "Controllers reaching Infrastructure: " + Describe(result));
    }

    /// <summary>
    /// Cada accion tiene cupo, y cada accion con sesion declara que tipo de actor la puede llamar. El
    /// feed `.ics` es la unica anonima y por eso no lleva actores: su credencial es la URL.
    /// </summary>
    [Fact]
    public void Every_action_declares_rate_limit_and_actor_types()
    {
        var missing = new List<string>();

        foreach (var controller in ApiAssembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t)))
        {
            foreach (
                var action in controller.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            {
                if (action.GetCustomAttribute<RateLimitAttribute>() is null)
                    missing.Add($"{controller.Name}.{action.Name} sin [RateLimit]");

                var anonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                var actors =
                    action.GetCustomAttribute<AllowActorTypesAttribute>() is not null
                    || controller.GetCustomAttribute<AllowActorTypesAttribute>() is not null;

                if (!anonymous && !actors)
                    missing.Add($"{controller.Name}.{action.Name} sin [AllowActorTypes]");
            }
        }

        Assert.True(missing.Count == 0, string.Join(" | ", missing));
    }
}
