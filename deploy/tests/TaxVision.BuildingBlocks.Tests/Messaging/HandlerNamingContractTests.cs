using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// Wolverine solo descubre handlers en clases que terminan en <c>Handler</c> o <c>Consumer</c>.
/// Una clase en plural (<c>...Handlers</c>, <c>...Consumers</c>) compila, no da error y nunca recibe
/// un mensaje. Ya pasó tres veces: el directorio de clientes de Notification, la cadena de auditoría
/// de Signature y los eventos de asientos de Auth (UserSlotPriorityDomainEventHandlers: el recálculo
/// de asientos y el evento de override nunca corrieron).
/// </summary>
public sealed class HandlerNamingContractTests
{
    private static readonly Regex PublicStaticClass = new(
        @"public\s+static\s+(?:partial\s+)?class\s+(\w+)",
        RegexOptions.Compiled
    );

    private static readonly Regex HandlerMethod = new(
        @"public\s+static\s+(?:async\s+)?[\w<>\[\],.\s?]+?\s(?:Handle|HandleAsync|Consume|ConsumeAsync)\s*\(",
        RegexOptions.Compiled
    );

    [Fact]
    public void Every_static_class_with_handle_methods_has_a_discoverable_name()
    {
        var src = Path.Combine(RepositoryRoot().FullName, "src");
        List<string> offenders = [];

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file))
                continue;

            var text = File.ReadAllText(file);
            var classes = PublicStaticClass.Matches(text);
            for (var i = 0; i < classes.Count; i++)
            {
                var name = classes[i].Groups[1].Value;
                var end = i + 1 < classes.Count ? classes[i + 1].Index : text.Length;
                var body = text[classes[i].Index..end];
                if (HandlerMethod.IsMatch(body) && !name.EndsWith("Handler") && !name.EndsWith("Consumer"))
                    offenders.Add($"{name} ({Path.GetRelativePath(src, file)})");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Clases con Handle/Consume que Wolverine no descubre (el nombre tiene que terminar en "
                + "Handler o Consumer, en singular):\n  "
                + string.Join("\n  ", offenders)
        );
    }

    private static bool IsGenerated(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        return path.Contains($"{sep}obj{sep}")
            || path.Contains($"{sep}bin{sep}")
            || path.Contains($"{sep}Migrations{sep}");
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }
}
