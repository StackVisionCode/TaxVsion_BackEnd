using System.Reflection;
using System.Runtime.CompilerServices;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// BB-12. El namespace de un tipo debe decir de qué ensamblado sale.
///
/// <para>
/// Antes de esta regla había <b>siete</b> namespaces repartidos entre dos ensamblados a la vez
/// (<c>RateLimiting</c>, <c>Tenancy</c>, <c>Common</c>, <c>Security</c>, <c>ActorTypeAuthorization</c>,
/// <c>Caching</c> y <c>Sessions</c>). El coste no era estético: al leer <c>using BuildingBlocks.Tenancy;</c>
/// no se podía saber si el tipo venía del core o de <c>.Web</c>, ni por tanto qué <c>ProjectReference</c>
/// hacía falta — el mismo síntoma que BB-15 vino a tapar añadiendo la referencia explícita a los 18 .Api.
/// </para>
///
/// <para>
/// La regla se verifica por reflexión sobre los ensamblados ya cargados, no leyendo el código fuente:
/// así también atrapa un tipo que llegue desde un paquete o un generador.
/// </para>
/// </summary>
public sealed class NamespaceAssemblyContractTests
{
    private static readonly Assembly Core = typeof(ActorType).Assembly;
    private static readonly Assembly Web = typeof(AllowActorTypesAttribute).Assembly;
    private static readonly Assembly Infrastructure = typeof(RedisCacheService).Assembly;

    /// <summary>
    /// Tipos que el compilador genera (clases de cierre, iteradores, <c>&lt;Module&gt;</c>) no llevan
    /// namespace propio ni los elige nadie, así que no participan del contrato.
    /// </summary>
    private static IEnumerable<string> DeclaredNamespaces(Assembly assembly) =>
        assembly
            .GetTypes()
            .Where(t => t.IsPublic || t.IsNestedPublic || t.IsNotPublic)
            .Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Where(t => !t.Name.StartsWith('<'))
            .Select(t => t.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .Select(ns => ns!)
            .Distinct();

    [Theory]
    [InlineData("BuildingBlocks.Web")]
    [InlineData("BuildingBlocks.Infrastructure")]
    public void CadaNamespaceLlevaElPrefijoDeSuEnsamblado(string prefix)
    {
        var assembly = prefix == "BuildingBlocks.Web" ? Web : Infrastructure;

        var offenders = DeclaredNamespaces(assembly)
            .Where(ns => !ns.StartsWith(prefix + ".", StringComparison.Ordinal))
            .OrderBy(ns => ns)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Estos namespaces viven en {prefix}.dll pero no lo declaran: {string.Join(", ", offenders)}. "
                + $"Un tipo de {prefix}.dll debe estar bajo {prefix}.*, para que el using diga de dónde sale."
        );
    }

    [Fact]
    public void NingunNamespaceEstaPartidoEntreEnsamblados()
    {
        var byAssembly = new (string Name, Assembly Assembly)[]
        {
            ("BuildingBlocks", Core),
            ("BuildingBlocks.Web", Web),
            ("BuildingBlocks.Infrastructure", Infrastructure),
        };

        var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, assembly) in byAssembly)
        {
            foreach (var ns in DeclaredNamespaces(assembly))
            {
                if (!owners.TryGetValue(ns, out var list))
                    owners[ns] = list = [];
                list.Add(name);
            }
        }

        var split = owners
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => $"{kv.Key} -> [{string.Join(", ", kv.Value)}]")
            .OrderBy(s => s)
            .ToArray();

        Assert.True(
            split.Length == 0,
            "Namespaces declarados desde más de un ensamblado: "
                + string.Join("; ", split)
                + ". Un using debe resolver a un solo ensamblado."
        );
    }
}
