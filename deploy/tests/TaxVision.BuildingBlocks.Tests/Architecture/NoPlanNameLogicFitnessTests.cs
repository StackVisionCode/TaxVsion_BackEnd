using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// Plan único D8: ninguna regla del backend decide por el nombre o el tier del plan; deciden los módulos y las claves
/// de la edición. Recorre <c>src/</c> buscando los códigos "starter", "pro" y "enterprise" y los usos de
/// <c>PlanTier.X</c>, <c>PlanCatalog.X</c> y <c>PlanTierResolver</c>. Solo quedan permitidos donde el nombre es dato
/// de arranque: el catálogo inicial, sus semillas, las revisiones del catálogo y la conversión de la columna.
/// </summary>
public sealed class NoPlanNameLogicFitnessTests
{
    private static readonly Regex PlanName = new(
        "\"(?:starter|pro|enterprise)\"|\\bPlanTier\\.(?:Starter|Pro|Enterprise|Trial)\\b"
            + "|\\bPlanCatalog\\.(?:Starter|Pro|Enterprise)\\b|\\bPlanTierResolver\\b",
        RegexOptions.IgnoreCase
    );

    // Rutas (relativas a src/, con '/') donde el nombre del plan es dato de arranque, no lógica.
    private static readonly string[] Allowed =
    [
        "Services/Subscription/TaxVision.Subscription.Domain/Plans/PlanCatalog.cs",
        "Services/Subscription/TaxVision.Subscription.Infrastructure/Persistence/SubscriptionPlanCatalogSeeder.cs",
        "Services/Subscription/TaxVision.Subscription.Application/Catalog/Revisions/",
        "Services/Subscription/TaxVision.Subscription.Infrastructure/Persistence/Converters/PlanTierConverter.cs",
        "Services/Subscription/TaxVision.Subscription.Infrastructure/Persistence/Configurations/PlanRateLimitConfiguration.cs",
        "Services/Subscription/TaxVision.Subscription.Application/Subscriptions/IntegrationEvents/SubscriptionOptions.cs",
    ];

    [Fact]
    public void No_backend_rule_decides_by_plan_name_or_tier()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (IsGenerated(relative) || Allowed.Any(allowed => relative.StartsWith(allowed, StringComparison.Ordinal)))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = WithoutComment(lines[i]);
                if (PlanName.IsMatch(code))
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Lógica por nombre o tier de plan fuera de la lista blanca (D8):\n" + string.Join("\n", offenders)
        );
    }

    private static bool IsGenerated(string relative) =>
        relative.Contains("/Migrations/", StringComparison.Ordinal)
        || relative.Contains("/bin/", StringComparison.Ordinal)
        || relative.Contains("/obj/", StringComparison.Ordinal);

    // Quita el comentario de línea si el "//" no está dentro de un string.
    private static string WithoutComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
                inString = !inString;
            else if (!inString && line[i] == '/' && line[i + 1] == '/')
                return line[..i];
        }

        return line;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
