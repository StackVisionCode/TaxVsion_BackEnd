using System.Text.RegularExpressions;
using BuildingBlocks.Authorization;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// A6/A5.6 — fitness functions del cableado del gate de módulo, a nivel de código fuente.
///
/// Hacen falta porque el fallo que cubren NO aparece al arrancar: la codegen de Wolverine es
/// <c>Dynamic</c>, así que el handler que consume <c>TenantEntitlementsChangedIntegrationEvent</c> se
/// compila cuando llega el primer mensaje. Un servicio que registre el invalidador sin registrar la
/// caché de módulos arranca perfecto y revienta la primera vez que un tenant cambia de plan — el peor
/// momento posible. Un test que solo levante el servicio no lo ve; este sí.
/// </summary>
public sealed class ModuleGateWiringFitnessTests
{
    [Fact]
    public void Every_invalidator_that_clears_the_modules_cache_has_it_registered()
    {
        // Los servicios SIN gate (Auth, Billing, Tenant, Sms…) tienen el invalidador solo por el rate
        // limit y no dependen de la caché de módulos — este test no los toca. La pareja que importa es
        // por servicio: si SU invalidador la limpia, SU DependencyInjection tiene que registrarla.
        var offenders = ServiceInfrastructureDirectories()
            .Where(dir =>
            {
                var invalidator = Path.Combine(dir, "RateLimiting", "TenantPlanCodeCacheInvalidator.cs");
                if (!File.Exists(invalidator))
                    return false;

                if (
                    !File.ReadAllText(invalidator)
                        .Contains("CachedTenantEntitlementModulesReader", StringComparison.Ordinal)
                )
                    return false;

                var di = Path.Combine(dir, "DependencyInjection.cs");
                return !File.Exists(di)
                    || !File.ReadAllText(di)
                        .Contains("AddCachedTenantEntitlementModulesReader", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These services have an invalidator that clears the modules cache but never register it "
                + "(use services.AddCachedTenantEntitlementModulesReader<...>()): "
                + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void Every_gated_service_invalidates_the_modules_cache_on_a_plan_change()
    {
        // La dirección contraria, y la que de verdad se nota: con la caché registrada pero sin
        // invalidar, un upgrade tarda hasta el TTL en surtir efecto y el tenant ve 403 en lo que acaba
        // de pagar. El TTL es el respaldo del evento perdido, no el camino normal.
        var offenders = ServiceInfrastructureDirectories()
            .Where(dir =>
            {
                var di = Path.Combine(dir, "DependencyInjection.cs");
                if (
                    !File.Exists(di)
                    || !File.ReadAllText(di)
                        .Contains("AddCachedTenantEntitlementModulesReader", StringComparison.Ordinal)
                )
                    return false;

                var invalidator = Path.Combine(dir, "RateLimiting", "TenantPlanCodeCacheInvalidator.cs");
                return !File.Exists(invalidator)
                    || !File.ReadAllText(invalidator)
                        .Contains("CachedTenantEntitlementModulesReader", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These services cache the modules but never invalidate that cache when the plan changes: "
                + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void No_service_registers_the_modules_reader_without_its_cache()
    {
        // El registro directo `AddScoped<ITenantEntitlementModulesReader, EfTenantEntitlementModulesReader>()`
        // es el que había antes en los 13 servicios: funciona, pero deja una query por request en cada
        // endpoint gateado y —lo importante— deja la caché fuera de la invalidación por evento, o sea
        // sin forma de enterarse de un cambio de plan. El helper hace las tres cosas de una vez.
        var offenders = ServiceDependencyInjectionFiles()
            .Where(file =>
                Regex.IsMatch(
                    File.ReadAllText(file),
                    @"AddScoped<\s*\n?\s*(BuildingBlocks\.RateLimiting\.)?ITenantEntitlementModulesReader\s*,"
                )
            )
            .Select(Path.GetFullPath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Register the modules reader through AddCachedTenantEntitlementModulesReader<...>() so it gets its "
                + "cache and event invalidation: "
                + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void The_fourteen_gated_services_are_still_wired()
    {
        // Contrapeso de los dos tests de arriba: ambos pasan trivialmente si el cableado DESAPARECE.
        // Este fija el número medido hoy, así que quitar el gate de un servicio obliga a decirlo acá.
        var wired = ServiceDependencyInjectionFiles()
            .Where(file =>
                File.ReadAllText(file).Contains("AddCachedTenantEntitlementModulesReader", StringComparison.Ordinal)
            )
            .ToList();

        Assert.Equal(14, wired.Count);
    }

    [Fact]
    public void The_node_exemption_list_mirrors_the_dotnet_one()
    {
        // Communication (Node) tiene su propia copia del mapa permiso->modulo, porque no puede
        // referenciar BuildingBlocks. Si las dos listas de exenciones divergen, el MISMO permiso da
        // 200 en un servicio y 403 en el otro, y el sintoma que ve el usuario es "el chat funciona
        // pero la campanita no" — imposible de atribuir sin comparar los dos ficheros a mano.
        //
        // Se compara contra el fichero fuente, no contra una lista escrita aca: una tercera copia
        // tendria el mismo problema que las dos que ya hay.
        var nodeMap = Path.Combine(
            RepoRoot(),
            "src",
            "Services",
            "Communication",
            "src",
            "domain",
            "shared",
            "permission-module-map.ts"
        );
        Assert.True(File.Exists(nodeMap), $"No se encontro el mapa de Node en {nodeMap}");

        var source = File.ReadAllText(nodeMap);
        var block = Regex.Match(
            source,
            @"const EXEMPT_PERMISSIONS:\s*ReadonlySet<string>\s*=\s*new Set\(\s*\[(?<body>.*?)\]\s*\)",
            RegexOptions.Singleline
        );
        Assert.True(block.Success, "No se pudo leer EXEMPT_PERMISSIONS del mapa de Node.");

        var fromNode = Regex
            .Matches(block.Groups["body"].Value, @"'(?<code>[^']+)'")
            .Select(match => match.Groups["code"].Value)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        var fromDotnet = PermissionModuleMap.Exempt.OrderBy(code => code, StringComparer.Ordinal).ToList();

        Assert.Equal(fromDotnet, fromNode);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException(
                "Could not locate the repo root (TaxVision.slnx) from the test output directory."
            );

        return dir.FullName;
    }

    private static IEnumerable<string> ServiceInfrastructureDirectories() =>
        ServiceDependencyInjectionFiles().Select(file => Path.GetDirectoryName(file)!);

    private static IEnumerable<string> ServiceDependencyInjectionFiles()
    {
        var dir = new DirectoryInfo(RepoRoot());

        return Directory
            .EnumerateFiles(
                Path.Combine(dir.FullName, "src", "Services"),
                "DependencyInjection.cs",
                SearchOption.AllDirectories
            )
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            );
    }
}
