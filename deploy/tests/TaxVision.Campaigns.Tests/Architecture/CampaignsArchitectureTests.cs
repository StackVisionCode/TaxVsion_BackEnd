using System.Reflection;
using TaxVision.Campaigns.Application;

namespace TaxVision.Campaigns.Tests.Architecture;

/// <summary>CMP-13: un handler orquesta; lo demás va en colaboradores con nombre.</summary>
public sealed class CampaignsArchitectureTests
{
    private const int MaxDependencies = 6;

    [Fact]
    public void Handlers_HaveAtMostSixDependencies()
    {
        var offenders = typeof(AssemblyMarker)
            .Assembly.GetTypes()
            .SelectMany(type =>
                type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            .Where(method => method.Name is "Handle" or "HandleAsync")
            .Select(method =>
                (
                    Name: $"{method.DeclaringType!.Name}({method.GetParameters()[0].ParameterType.Name})",
                    Count: method.GetParameters().Skip(1).Count(p => p.ParameterType != typeof(CancellationToken))
                )
            )
            .Where(handler => handler.Count > MaxDependencies)
            .Select(handler => $"{handler.Name}: {handler.Count}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0, "Handlers con más de 6 dependencias:\n  " + string.Join("\n  ", offenders));
    }

    // D-CMP-2: la audiencia sale por HTTP de Customer. CustomerDirectoryClient es ese cliente y se queda.
    [Fact]
    public void NoCustomerDirectoryProjectionTypes()
    {
        var offenders = new[]
        {
            typeof(AssemblyMarker).Assembly,
            typeof(TaxVision.Campaigns.Infrastructure.Persistence.CampaignsDbContext).Assembly,
        }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace?.EndsWith(".Migrations", StringComparison.Ordinal) != true)
            .Select(type => type.Name)
            .Where(name => name.Contains("CustomerDirectory", StringComparison.Ordinal))
            .Where(name => !name.Contains("CustomerDirectoryClient", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0, "Proyección retirada:\n  " + string.Join("\n  ", offenders));
    }
}
