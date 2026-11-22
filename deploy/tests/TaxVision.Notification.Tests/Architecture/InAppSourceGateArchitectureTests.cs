using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests.Architecture;

public sealed class InAppSourceGateArchitectureTests
{
    // Un grupo que ningún consumer consulta es un flag que parece hacer algo y no hace nada.
    [Fact]
    public void Every_in_app_source_group_is_consulted_by_a_consumer()
    {
        var sources = NotificationDispatchArchitectureTests
            .ApplicationFiles()
            .Where(file => !file.EndsWith("InAppSourceGate.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        var unused = Enum.GetNames<InAppSourceGroup>()
            .Where(group =>
                !sources.Any(text =>
                    text.Contains($"PublishesInApp(InAppSourceGroup.{group})", StringComparison.Ordinal)
                )
            )
            .ToArray();

        Assert.True(unused.Length == 0, "Grupo in-app sin consumer que lo consulte:\n" + string.Join("\n", unused));
    }
}
