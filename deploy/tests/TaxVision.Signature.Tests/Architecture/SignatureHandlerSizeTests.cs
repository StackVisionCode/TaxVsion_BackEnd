namespace TaxVision.Signature.Tests.Architecture;

/// <summary>
/// SIG-13. Un handler o consumer orquesta: si pasa de 400 líneas, lo que crece va a un colaborador. La deuda de hoy
/// queda listada con su motivo; al partirla, la entrada se retira.
/// </summary>
public sealed class SignatureHandlerSizeTests
{
    private const int MaxLines = 400;

    private const string LegalChain = "Cadena de auditoría legal: SIG-13 no la toca";
    private const string OutOfItem = "Creció después del hallazgo; fuera de los tres archivos de SIG-13";

    private static readonly IReadOnlyDictionary<string, string> PendingAdoption = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
        ["Audit/AuditChainAppenderConsumer.cs"] = LegalChain,
        ["Delivery/SignerPartialCopyConsumer.cs"] = OutOfItem,
        ["Requests/Commands/UpsertDraft/UpsertSignatureDraftHandler.cs"] = OutOfItem,
    };

    private static string ApplicationDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "Services", "Signature", "TaxVision.Signature.Application");
    }

    private static IEnumerable<(string Path, int Lines)> HandlersAndConsumers()
    {
        var root = ApplicationDirectory();
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file =>
                file.EndsWith("Handler.cs", StringComparison.Ordinal)
                || file.EndsWith("Consumer.cs", StringComparison.Ordinal)
            )
            .Select(file => (Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllLines(file).Length));
    }

    [Fact]
    public void HandlersAndConsumersUnder400Lines()
    {
        var offenders = HandlersAndConsumers()
            .Where(file => file.Lines > MaxLines && !PendingAdoption.ContainsKey(file.Path))
            .Select(file => $"{file.Path}: {file.Lines}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0, $"Pasan de {MaxLines} líneas:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void PendingAdoption_EntriesAreStillNeeded()
    {
        var found = HandlersAndConsumers().ToDictionary(file => file.Path, file => file.Lines);
        var stale = PendingAdoption
            .Keys.Where(path => found.GetValueOrDefault(path) <= MaxLines)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(stale.Length == 0, "Entradas ya partidas:\n  " + string.Join("\n  ", stale));
    }
}
