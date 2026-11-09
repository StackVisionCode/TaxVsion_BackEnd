using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Testing.Architecture;

/// <summary>
/// BB-3. Un job (<c>BackgroundService</c>, <c>IHostedService</c> o clase <c>*Job</c>) no publica por un
/// <c>IMessageBus</c> suelto (del scope, propio o inyectado): ese aviso sale fuera de la transacción que
/// guardó el cambio y se pierde si el proceso cae entre los dos pasos. Publica por el outbox de su unidad
/// (<c>TransactionalJobUnit</c>) o despacha un comando por entidad con <c>InvokeAsync</c>.
///
/// <para><b>Patrón aceptado — <c>InvokeAsync</c> por entidad</b> (con <c>new MessageBus(runtime)</c> o con el
/// bus del scope): cada invocación corre el handler en el pipeline de Wolverine, con su scope, su
/// transacción (<c>AutoApplyTransactions</c>) y su outbox. Es una unidad por entidad, igual que BB-3, así
/// que no hace falta migrarlo. Condición: el handler invocado es transaccional y el job no publica nada
/// por su cuenta.</para>
///
/// <para>El servicio declara su carpeta y su deuda:
/// <code>
/// public sealed class TasksJobPublishingTests : JobPublishingRulesBase
/// {
///     protected override string SourceDirectory => "src/Services/Tasks";
/// }
/// </code></para>
///
/// <para>Límite: la revisión es por archivo. Si el job pasa el bus a un helper de otro archivo y ese helper
/// publica, no se detecta.</para>
/// </summary>
public abstract class JobPublishingRulesBase
{
    /// <summary>Carpeta a revisar, relativa a la raíz del repo (por ejemplo <c>src/Services/Tasks</c>).</summary>
    protected abstract string SourceDirectory { get; }

    /// <summary>
    /// Deuda pendiente de adopción: ruta relativa (con <c>/</c>) y el ítem del plan que la corrige.
    /// Tiene que seguir fallando: al adoptar BB-3, la entrada se retira.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string> PendingAdoption { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Excepciones permanentes con su motivo. Solo se exige que el archivo exista.</summary>
    protected virtual IReadOnlyDictionary<string, string> JustifiedExceptions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void Jobs_DoNotPublishThroughALooseMessageBus()
    {
        var offenders = JobPublishingRules
            .Scan(SourceDirectory)
            .Where(f => f.Kind == JobPublishingKind.PublishesThroughLooseBus)
            .Where(f => !PendingAdoption.ContainsKey(f.Path) && !JustifiedExceptions.ContainsKey(f.Path))
            .Select(f => f.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Jobs que publican por un IMessageBus suelto, fuera de la transacción que guarda el cambio. "
                + "Usar TransactionalJobUnit (BB-3) o InvokeAsync por entidad:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    [Fact]
    public void PendingAdoption_EntriesAreStillNeeded()
    {
        var flagged = JobPublishingRules
            .Scan(SourceDirectory)
            .Where(f => f.Kind == JobPublishingKind.PublishesThroughLooseBus)
            .Select(f => f.Path)
            .ToHashSet(StringComparer.Ordinal);

        var stale = PendingAdoption.Keys.Where(p => !flagged.Contains(p)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            stale.Length == 0,
            "Estas entradas de PendingAdoption ya no publican por un bus suelto (o no existen). Retirarlas:\n  "
                + string.Join("\n  ", stale)
        );
    }

    [Fact]
    public void JustifiedExceptions_PointToExistingFiles()
    {
        var root = JobPublishingRules.RepositoryRoot();
        var missing = JustifiedExceptions
            .Keys.Where(p => !File.Exists(Path.Combine(root, p)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Excepciones que apuntan a archivos inexistentes:\n  " + string.Join("\n  ", missing)
        );
    }
}

public enum JobPublishingKind
{
    /// <summary>Job sin bus propio (por ejemplo, ya usa el outbox de su unidad).</summary>
    NoLooseBus,

    /// <summary>Patrón aceptado: solo <c>InvokeAsync</c> por entidad.</summary>
    InvokesPerEntity,

    /// <summary>
    /// Tiene un bus y lo entrega a otro método (de otro archivo) que no se puede revisar desde aquí.
    /// No es violación; se revisa a mano al adoptar.
    /// </summary>
    BusHandedOff,

    /// <summary>Publica por un bus suelto: violación.</summary>
    PublishesThroughLooseBus,
}

public sealed record JobPublishingFinding(string Path, JobPublishingKind Kind);

/// <summary>Revisión por texto de <see cref="JobPublishingRulesBase"/>, suelta para reglas propias.</summary>
public static class JobPublishingRules
{
    private static readonly Regex ClassHeader = new(
        @"\bclass\s+(?<name>\w+)(?<header>[^{;]*)[{;]",
        RegexOptions.Compiled
    );
    private static readonly Regex HostedBase = new(
        @"(BackgroundService|HostedService)\b|[:,]\s*[\w.]*Job\b",
        RegexOptions.Compiled
    );

    // Nombres que apuntan a un IMessageBus: parámetros, campos, propiedades y variables.
    private static readonly Regex TypedBusName = new(
        @"\b(?:Wolverine\.)?I?MessageBus\??\s+(?<name>\w+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex ResolvedBusName = new(
        @"\bvar\s+(?<name>\w+)\s*=\s*[^;]*?(?:GetRequiredService|GetService)\s*<\s*(?:Wolverine\.)?IMessageBus\s*>|\bvar\s+(?<name>\w+)\s*=\s*new\s+MessageBus\s*\(",
        RegexOptions.Compiled
    );

    // Publicar: los métodos de Wolverine y las extensiones/helpers con el mismo prefijo.
    private const string PublishMethods = @"(?:Publish|Send|Schedule|Broadcast)\w*Async|EndpointFor";

    private const string InlineBus =
        @"(?:GetRequiredService|GetService)\s*<\s*(?:Wolverine\.)?IMessageBus\s*>\s*\(\s*\)";
    private static readonly Regex InlineResolvedBus = new(InlineBus, RegexOptions.Compiled);
    private static readonly Regex InlineResolvedPublish = new(
        $@"{InlineBus}\s*\.\s*(?:{PublishMethods})\b",
        RegexOptions.Compiled
    );
    private static readonly Regex InlineResolvedInvoke = new(
        $@"{InlineBus}\s*\.\s*InvokeAsync\b",
        RegexOptions.Compiled
    );

    // Helper de otra clase que recibe el bus, por ejemplo Publisher.PublishReceiptAsync(payment, bus, ...).
    private static readonly Regex PublishHelperCall = new(
        @"\b[\w.]+\s*\.\s*(?:Publish|Send|Schedule|Broadcast)\w*Async\s*(?:<[^>()]*>)?\s*\(",
        RegexOptions.Compiled
    );
    private static readonly Regex Comments = new(
        @"//[^\n]*|/\*.*?\*/",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    /// <summary>Clasifica los jobs de la carpeta (relativa a la raíz del repo).</summary>
    public static IReadOnlyList<JobPublishingFinding> Scan(string sourceDirectory)
    {
        var root = RepositoryRoot();
        var directory = Path.Combine(root, sourceDirectory);
        Assert.True(Directory.Exists(directory), $"No existe la carpeta {sourceDirectory}.");

        return Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f))
            .Select(f => (Path: Relative(root, f), Kind: Classify(File.ReadAllText(f))))
            .Where(f => f.Kind is not null)
            .Select(f => new JobPublishingFinding(f.Path, f.Kind!.Value))
            .ToArray();
    }

    /// <summary>Nulo si el archivo no declara un job.</summary>
    public static JobPublishingKind? Classify(string source)
    {
        var code = Comments.Replace(source, "");
        if (!IsHostedJob(code))
            return null;

        var busNames = TypedBusName
            .Matches(code)
            .Concat(ResolvedBusName.Matches(code))
            .Select(m => m.Groups["name"].Value)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var resolvesInline = InlineResolvedBus.IsMatch(code);
        if (busNames.Count == 0 && !resolvesInline)
            return JobPublishingKind.NoLooseBus;

        var publishes =
            InlineResolvedPublish.IsMatch(code)
            || busNames.Any(n => Regex.IsMatch(code, $@"\b{Regex.Escape(n)}\s*\??\.\s*(?:{PublishMethods})\b"))
            || PublishHelperCall
                .Matches(code)
                .Select(m => Arguments(code, m.Index + m.Length))
                .Any(args =>
                    InlineResolvedBus.IsMatch(args) || busNames.Any(n => Regex.IsMatch(args, $@"\b{Regex.Escape(n)}\b"))
                );
        if (publishes)
            return JobPublishingKind.PublishesThroughLooseBus;

        var invokes =
            InlineResolvedInvoke.IsMatch(code)
            || busNames.Any(n => Regex.IsMatch(code, $@"\b{Regex.Escape(n)}\s*\??\.\s*InvokeAsync\b"));
        return invokes ? JobPublishingKind.InvokesPerEntity : JobPublishingKind.BusHandedOff;
    }

    public static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static bool IsHostedJob(string code) =>
        ClassHeader
            .Matches(code)
            .Any(m =>
                m.Groups["name"].Value.EndsWith("Job", StringComparison.Ordinal)
                || HostedBase.IsMatch(m.Groups["header"].Value)
            );

    /// <summary>Texto de los argumentos desde justo después de <c>(</c> hasta su <c>)</c>.</summary>
    private static string Arguments(string code, int start)
    {
        var depth = 1;
        for (var i = start; i < code.Length; i++)
        {
            if (code[i] == '(')
                depth++;
            else if (code[i] == ')' && --depth == 0)
                return code[start..i];
        }
        return code[start..];
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
