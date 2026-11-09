using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace TaxVision.BuildingBlocks.Testing.Architecture;

/// <summary>
/// BB-6. Reglas de capas de la guía, listas para heredar: Domain sin EF, ASP.NET ni Wolverine;
/// Application sin Infrastructure ni EF. El servicio solo declara sus ensamblados:
/// <code>
/// public sealed class TasksLayeringTests : LayeringRulesBase
/// {
///     protected override Assembly DomainAssembly => typeof(TaskItem).Assembly;
///     protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
/// }
/// </code>
/// </summary>
public abstract class LayeringRulesBase
{
    protected abstract Assembly DomainAssembly { get; }

    protected abstract Assembly ApplicationAssembly { get; }

    /// <summary>Por defecto, el nombre del ensamblado Application con <c>.Infrastructure</c>.</summary>
    protected virtual string InfrastructureNamespace =>
        LayeringRules.InfrastructureNamespaceFor(ApplicationAssembly.GetName().Name!);

    /// <summary>
    /// Deuda conocida al adoptar: nombre del tipo, tal como lo imprime el fallo, y su motivo.
    /// Cada entrada se retira cuando se corrige.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string> Exemptions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void Domain_ShouldNotDependOn_EntityFramework() =>
        AssertNoDependency("Domain", DomainAssembly, LayeringRules.EntityFramework);

    [Fact]
    public void Domain_ShouldNotDependOn_AspNetCore() =>
        AssertNoDependency("Domain", DomainAssembly, LayeringRules.AspNetCore);

    [Fact]
    public void Domain_ShouldNotDependOn_Wolverine() =>
        AssertNoDependency("Domain", DomainAssembly, LayeringRules.Wolverine);

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure() =>
        AssertNoDependency("Application", ApplicationAssembly, InfrastructureNamespace);

    [Fact]
    public void Application_ShouldNotDependOn_EntityFramework() =>
        AssertNoDependency("Application", ApplicationAssembly, LayeringRules.EntityFramework);

    private void AssertNoDependency(string layer, Assembly assembly, string forbiddenNamespace)
    {
        var violations = LayeringRules.FindViolations(assembly, forbiddenNamespace, Exemptions);

        Assert.True(
            violations.Count == 0,
            $"{layer} ({assembly.GetName().Name}) depende de {forbiddenNamespace}:\n  "
                + string.Join("\n  ", violations)
        );
    }
}

/// <summary>Chequeos de <see cref="LayeringRulesBase"/>, sueltos para reglas propias del servicio.</summary>
public static class LayeringRules
{
    public const string EntityFramework = "Microsoft.EntityFrameworkCore";
    public const string AspNetCore = "Microsoft.AspNetCore";
    public const string Wolverine = "Wolverine";

    private const string ApplicationSuffix = ".Application";

    /// <summary>Tipos del ensamblado que dependen del namespace prohibido, sin los exentos.</summary>
    public static IReadOnlyList<string> FindViolations(
        Assembly assembly,
        string forbiddenNamespace,
        IReadOnlyDictionary<string, string>? exemptions = null
    )
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn(forbiddenNamespace).GetResult();

        return (result.FailingTypeNames ?? [])
            .Where(name => exemptions is null || !exemptions.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public static string InfrastructureNamespaceFor(string applicationAssemblyName)
    {
        if (!applicationAssemblyName.EndsWith(ApplicationSuffix, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"'{applicationAssemblyName}' no termina en {ApplicationSuffix}: "
                    + "sobrescribir InfrastructureNamespace en la clase de pruebas."
            );

        return applicationAssemblyName[..^ApplicationSuffix.Length] + ".Infrastructure";
    }
}
