using System.Reflection;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;
using TaxVision.Reminder.Application.Reminders.Abstractions;

namespace TaxVision.Reminder.Tests.Architecture;

/// <summary>
/// Mismo patrón que las 15 copias del monorepo (ver <c>NotesArchitectureTests</c>): rompe el build
/// si una acción de controller queda sin <see cref="AllowActorTypesAttribute"/> o sin
/// <see cref="RateLimitAttribute"/>/<see cref="RateLimitExemptAttribute"/>.
///
/// <para>
/// Las dos primeras reglas se adelantaron a la Fase 3, cuando Reminder tenía <b>cero</b> controllers
/// y pasaban de vacío: el punto era que el guardrail existiera ANTES de que la Fase 6 escribiera el
/// primer endpoint. Por eso están ancladas en <c>typeof(Program)</c> y no en un controller concreto.
/// La Fase 9 suma las reglas de capas y la regla propia del servicio (ADR-R-01).
/// </para>
/// </summary>
public sealed class ReminderArchitectureTests
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;
    private static readonly Assembly DomainAssembly = typeof(ReminderAggregate).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IReminderMetrics).Assembly;

    [Fact]
    public void Controller_actions_should_declare_AllowActorTypes()
    {
        var violations = FindActionsMissingAllowActorTypes(ApiAssembly);
        Assert.True(
            violations.Count == 0,
            "Actions missing [AllowActorTypes] (method or controller level): " + string.Join(", ", violations)
        );
    }

    [Fact]
    public void Controller_actions_should_declare_RateLimit_or_RateLimitExempt()
    {
        var violations = FindActionsMissingRateLimit(ApiAssembly);
        Assert.True(
            violations.Count == 0,
            "Actions missing [RateLimit] or [RateLimitExempt] (method or controller level): "
                + string.Join(", ", violations)
        );
    }

    [Fact]
    public void Controllers_should_not_depend_on_Infrastructure()
    {
        var result = Types
            .InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("TaxVision.Reminder.Api.Controllers")
            .ShouldNot()
            .HaveDependencyOn("TaxVision.Reminder.Infrastructure")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "Controllers depending on Infrastructure: "
                + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())
        );
    }

    /// <summary>
    /// El aggregate decide cuándo disparar y cuándo descartar; traducir eso a triggers es de
    /// Infrastructure. Si Domain llegara a referenciar Quartz o EF, esa decisión de negocio quedaría
    /// imposible de probar sin scheduler ni base de datos, que es justo lo que hoy la hace barata.
    /// </summary>
    [Theory]
    [InlineData("Quartz")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("TaxVision.Reminder.Infrastructure")]
    public void Domain_should_not_depend_on_infrastructure_concerns(string forbiddenNamespace)
    {
        var result = Types.InAssembly(DomainAssembly).ShouldNot().HaveDependencyOn(forbiddenNamespace).GetResult();

        Assert.True(result.IsSuccessful, $"Domain types depending on {forbiddenNamespace}: " + Describe(result));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("TaxVision.Reminder.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, "Application types depending on Infrastructure: " + Describe(result));
    }

    /// <summary>
    /// <b>Regla propia de este servicio (ADR-R-01).</b> Reminder no conoce Calendar ni Task: recibe
    /// <c>reminder.target_moved.v1</c>/<c>reminder.target_closed.v1</c>, contratos genéricos con un
    /// <c>TargetId</c> opaco. Consumir directamente los eventos de esos contextos volvería a atar
    /// Reminder a su modelo — el acoplamiento que el bounded context vino a romper, y que se cuela
    /// con un solo <c>using</c> "de paso".
    ///
    /// <para>
    /// Se evalúa contra los <b>tres</b> ensamblados, no solo Application: el bug se cuela por donde
    /// nadie mira, y un mapper en Infrastructure o un DTO en Api son sitios igual de plausibles.
    /// </para>
    ///
    /// <para>
    /// <b>Hoy pasa de vacío y hay que decirlo.</b> Ninguno de los dos namespaces existe todavía —
    /// Calendar y Task no están creados — y NetArchTest no distingue «no hay dependencia» de «el
    /// namespace no existe». La regla no prueba nada <i>ahora</i>; su trabajo empieza el día que esos
    /// contratos aparezcan, que es justamente el día en que alguien va a estar tentado de
    /// consumirlos directo. Se deja puesta por el mismo criterio que las dos reglas de controllers,
    /// escritas en la Fase 3 con cero controllers.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("BuildingBlocks.Messaging.CalendarIntegrationEvents")]
    [InlineData("BuildingBlocks.Messaging.TaskIntegrationEvents")]
    public void No_Reminder_type_should_reference_a_neighbouring_bounded_context(string forbiddenNamespace)
    {
        foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly, ApiAssembly })
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn(forbiddenNamespace).GetResult();

            Assert.True(
                result.IsSuccessful,
                $"{assembly.GetName().Name} depends on {forbiddenNamespace} (ADR-R-01): " + Describe(result)
            );
        }
    }

    private static string Describe(TestResult result) =>
        string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>());

    private static List<string> FindActionsMissingAllowActorTypes(Assembly apiAssembly)
    {
        var violations = new List<string>();
        foreach (var controllerType in ControllerTypes(apiAssembly))
        {
            var classIsAnonymous =
                controllerType.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;
            var classAllowActorTypes = controllerType.GetCustomAttribute<AllowActorTypesAttribute>(inherit: true);

            foreach (var action in Actions(controllerType))
            {
                if (classIsAnonymous || action.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                    continue;

                var allowActorTypes = action.GetCustomAttribute<AllowActorTypesAttribute>() ?? classAllowActorTypes;
                if (allowActorTypes is null)
                    violations.Add($"{controllerType.FullName}.{action.Name}");
            }
        }

        return violations;
    }

    private static List<string> FindActionsMissingRateLimit(Assembly apiAssembly)
    {
        var violations = new List<string>();
        foreach (var controllerType in ControllerTypes(apiAssembly))
        {
            var classRateLimit = controllerType.GetCustomAttribute<RateLimitAttribute>(inherit: true);
            var classRateLimitExempt = controllerType.GetCustomAttribute<RateLimitExemptAttribute>(inherit: true);

            foreach (var action in Actions(controllerType))
            {
                if (!action.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
                    continue;

                var rateLimit = action.GetCustomAttribute<RateLimitAttribute>() ?? classRateLimit;
                var rateLimitExempt = action.GetCustomAttribute<RateLimitExemptAttribute>() ?? classRateLimitExempt;

                if (rateLimit is null && rateLimitExempt is null)
                    violations.Add($"{controllerType.FullName}.{action.Name}");
            }
        }

        return violations;
    }

    private static IEnumerable<Type> ControllerTypes(Assembly apiAssembly) =>
        Types.InAssembly(apiAssembly).That().Inherit(typeof(ControllerBase)).And().AreClasses().GetTypes();

    private static IEnumerable<MethodInfo> Actions(Type controllerType) =>
        controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);
}
