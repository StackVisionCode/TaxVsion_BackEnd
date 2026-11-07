using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// Wolverine junta en un solo handler generado todos los <c>Handle</c> del mismo mensaje dentro de un servicio, y un
/// <c>[NonTransactional]</c> en uno le quita la transacción a los demás. Así se perdió la línea
/// <c>RequestCompleted</c> de la auditoría de Signature: el sellado no quería transacción y la auditoría la exige.
/// Lee el código, como <see cref="HandlerNamingContractTests"/>, para cubrir también a los servicios sin host de pruebas.
/// </summary>
public sealed class HandlerTransactionContractTests
{
    private static readonly Regex ClassDeclaration = new(@"\bclass\s+(\w+)", RegexOptions.Compiled);

    private static readonly Regex NonTransactionalAttribute = new(
        @"^[ \t]*\[NonTransactional\]",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    private static readonly Regex HandlerMethod = new(
        @"(?<attributes>(?:^[ \t]*\[[^\n]*\]\s*)*)^[ \t]*public\s+(?:static\s+)?(?:async\s+)?[\w<>\[\],.\s?()]+?\s"
            + @"(?:Handle|HandleAsync|Consume|ConsumeAsync)\s*\(\s*(?<message>[\w.]+(?:<[^()]*?>)?)\s+\w+",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    [Fact]
    public void NonTransactional_handler_next_to_a_transactional_one_is_reported()
    {
        const string source = """
            public static class SealingConsumer
            {
                [NonTransactional]
                public static Task Handle(
                    Completed evt,
                    CancellationToken ct
                ) => Task.CompletedTask;
            }

            public static class AuditConsumer
            {
                public static async Task Handle(Completed evt, IUnitOfWork unitOfWork) { }
            }
            """;

        var offenders = MixedHandlers(ParseHandlers("Sample", source));

        Assert.Equal("Sample: Completed", Assert.Single(offenders));
    }

    [Fact]
    public void Same_message_in_another_service_is_not_mixed()
    {
        const string sealing = """
            public static class SealingConsumer
            {
                [NonTransactional]
                public static Task Handle(Completed evt) => Task.CompletedTask;
            }
            """;
        const string audit = """
            public static class AuditConsumer
            {
                public static Task Handle(Completed evt) => Task.CompletedTask;
            }
            """;

        var offenders = MixedHandlers([.. ParseHandlers("One", sealing), .. ParseHandlers("Two", audit)]);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_message_keeps_one_transaction_mode_inside_each_service()
    {
        var services = Path.Combine(RepositoryRoot().FullName, "src", "Services");
        List<HandlerDeclaration> handlers = [];
        List<string> unread = [];

        foreach (var file in Directory.EnumerateFiles(services, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file))
                continue;

            var relative = Path.GetRelativePath(services, file);
            var source = File.ReadAllText(file);
            var found = ParseHandlers(relative.Split(Path.DirectorySeparatorChar)[0], source);
            handlers.AddRange(found);

            // Un atributo que esta prueba no supo leer la dejaría en verde sin mirar ese handler.
            if (NonTransactionalAttribute.Count(source) != found.Count(handler => handler.NonTransactional))
                unread.Add(relative);
        }

        Assert.True(
            unread.Count == 0,
            "[NonTransactional] fuera de un Handle reconocible:\n  " + string.Join("\n  ", unread)
        );

        var offenders = MixedHandlers(handlers);
        Assert.True(
            offenders.Count == 0,
            "Mensajes con un handler [NonTransactional] junto a otro que no lo es. Wolverine los ejecuta en un solo "
                + "handler y ninguno tiene transacción. Reenviar a un comando local el que debe correr sin ella:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    private static List<HandlerDeclaration> ParseHandlers(string service, string source)
    {
        var classes = ClassDeclaration.Matches(source);
        return HandlerMethod
            .Matches(source)
            .Where(method => IsDiscoverable(EnclosingClass(classes, method.Index)))
            .Select(method => new HandlerDeclaration(
                service,
                method.Groups["message"].Value.Split('.')[^1],
                NonTransactionalAttribute.IsMatch(method.Groups["attributes"].Value)
            ))
            .ToList();
    }

    private static string? EnclosingClass(MatchCollection classes, int index) =>
        classes.LastOrDefault(declaration => declaration.Index < index)?.Groups[1].Value;

    private static bool IsDiscoverable(string? className) =>
        className is not null && (className.EndsWith("Handler") || className.EndsWith("Consumer"));

    private static List<string> MixedHandlers(IEnumerable<HandlerDeclaration> handlers) =>
        handlers
            .GroupBy(handler => (handler.Service, handler.Message))
            .Where(group =>
                group.Any(handler => handler.NonTransactional) && group.Any(handler => !handler.NonTransactional)
            )
            .Select(group => $"{group.Key.Service}: {group.Key.Message}")
            .Order()
            .ToList();

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

    private sealed record HandlerDeclaration(string Service, string Message, bool NonTransactional);
}
