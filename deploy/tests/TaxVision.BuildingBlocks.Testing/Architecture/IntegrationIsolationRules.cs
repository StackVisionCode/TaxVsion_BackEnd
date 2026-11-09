using System.Text.RegularExpressions;

namespace TaxVision.BuildingBlocks.Testing.Architecture;

public enum IntegrationIsolationKind
{
    /// <summary>Host real sobre la base de la flota con la durabilidad activa: recoge sus sobres y deja los suyos.</summary>
    HostUsesFleetEnvelopes,

    /// <summary>Host real con Rabbit encendido: le quita mensajes a la flota.</summary>
    HostKeepsExternalTransports,

    /// <summary>Host real sobre la base de la flota con los jobs propios encendidos.</summary>
    HostKeepsJobs,

    /// <summary>Host de pruebas con sus sobres en el esquema que lee la flota.</summary>
    EnvelopesInFleetSchema,

    /// <summary>La prueba escribe filas en la base de la flota: una corrida abortada las deja ahí.</summary>
    WritesToFleetDatabase,
}

public sealed record IntegrationIsolationFinding(string Path, IntegrationIsolationKind Kind)
{
    public string Key => $"{Kind} {Path}";
}

/// <summary>
/// BB-10. Una T-INT abortada no puede dejar nada que la flota recoja (BIL-14). Clasifica cada archivo de pruebas por
/// la base que usa: la de la flota (user-secrets sin cambiar el catálogo) o una temporal.
///
/// <para><b>Límites.</b> Lee el texto y resuelve las clases de ayuda solo por su nombre. Una conexión que llegue por
/// otro camino no se ve. Un archivo que usa las dos bases cuenta como temporal.</para>
/// </summary>
public static partial class IntegrationIsolationRules
{
    private static readonly string[] FleetSchemas = ["dbo", "integration"];

    // Nombres que también son palabras de cualquier comentario: no dicen nada de la base.
    private static readonly string[] GenericNames = ["Fixture", "Host", "TemporaryDatabase"];

    [GeneratedRegex(@"AddUserSecrets|\w+_DB_CONNECTION|CORRESPONDENCE_SQLSERVER_TESTS")]
    private static partial Regex FleetConnectionSource();

    [GeneratedRegex(@"InitialCatalog\s*=|CREATE DATABASE|new TemporaryTestDatabase\(|TemporaryTestDatabase \w+ = new\(|UseSetting\(\s*""ConnectionStrings:Default""|\[""ConnectionStrings:Default""\]\s*=")]
    private static partial Regex TemporaryCatalog();

    [GeneratedRegex(@"\b(?:class|record)\s+(\w+)")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@":\s*WebApplicationFactory<Program>")]
    private static partial Regex RealHost();

    [GeneratedRegex(@"PersistMessagesWithSqlServer\(\s*[^,)]+(?:,\s*(?<schema>[^,)]+))?\)")]
    private static partial Regex EnvelopeStore();

    [GeneratedRegex(@"SaveChanges(?:Async)?\(|ExecuteNonQuery|ExecuteAsync\(|INSERT INTO|DELETE FROM|UPDATE \w+ SET")]
    private static partial Regex RowWrite();

    /// <summary>Hallazgos de todos los <c>.cs</c> de una carpeta relativa a la raíz del repo.</summary>
    public static IReadOnlyList<IntegrationIsolationFinding> Scan(string testsDirectory)
    {
        var root = JobPublishingRules.RepositoryRoot();
        var sources = Directory
            .EnumerateFiles(Path.Combine(root, testsDirectory), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGenerated(path))
            .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText);

        var helpers = sources
            .SelectMany(file =>
                TypeDeclaration().Matches(file.Value).Select(type => (Name: type.Groups[1].Value, Source: file.Value))
            )
            .Where(type => !GenericNames.Contains(type.Name))
            .Where(type => type.Name.EndsWith("Database", StringComparison.Ordinal)
                || type.Name.EndsWith("Db", StringComparison.Ordinal)
                || type.Name.EndsWith("Host", StringComparison.Ordinal)
                || type.Name.EndsWith("SqlServer", StringComparison.Ordinal)
                || type.Name.EndsWith("Fixture", StringComparison.Ordinal))
            .ToArray();
        var temporaryHelpers = helpers.Where(t => TemporaryCatalog().IsMatch(t.Source)).Select(t => t.Name).ToHashSet();
        var fleetHelpers = helpers
            .Where(t => FleetConnectionSource().IsMatch(t.Source) && !TemporaryCatalog().IsMatch(t.Source))
            .Select(t => t.Name)
            .ToHashSet();

        return sources
            .Where(file => !IsTheRuleItself(file.Key))
            .SelectMany(file => Classify(file.Value, file.Key, temporaryHelpers, fleetHelpers))
            .ToArray();
    }

    /// <summary>Clasifica una fuente. Las clases de ayuda dicen a qué base lleva usarlas.</summary>
    public static IReadOnlyList<IntegrationIsolationFinding> Classify(
        string source,
        string path = "",
        IReadOnlySet<string>? temporaryHelpers = null,
        IReadOnlySet<string>? fleetHelpers = null
    )
    {
        var temporary = TemporaryCatalog().IsMatch(source) || Mentions(source, temporaryHelpers);
        var fleet = !temporary && (FleetConnectionSource().IsMatch(source) || Mentions(source, fleetHelpers));
        var findings = new List<IntegrationIsolationFinding>();

        if (RealHost().IsMatch(source))
        {
            if (!source.Contains("DisableAllExternalWolverineTransports", StringComparison.Ordinal))
                findings.Add(new(path, IntegrationIsolationKind.HostKeepsExternalTransports));

            if (!temporary)
            {
                if (
                    !source.Contains("DurabilityMode.MediatorOnly", StringComparison.Ordinal)
                    && !source.Contains("DisableAllWolverineMessagePersistence", StringComparison.Ordinal)
                )
                    findings.Add(new(path, IntegrationIsolationKind.HostUsesFleetEnvelopes));

                if (!source.Contains("IHostedService", StringComparison.Ordinal))
                    findings.Add(new(path, IntegrationIsolationKind.HostKeepsJobs));
            }
        }

        if (fleet)
        {
            if (EnvelopeStore().Matches(source).Any(UsesFleetSchema))
                findings.Add(new(path, IntegrationIsolationKind.EnvelopesInFleetSchema));

            if (RowWrite().IsMatch(source))
                findings.Add(new(path, IntegrationIsolationKind.WritesToFleetDatabase));
        }

        return findings;
    }

    private static bool UsesFleetSchema(Match store)
    {
        var schema = store.Groups["schema"];
        if (!schema.Success)
            return true;

        var value = schema.Value.Trim();
        return value.EndsWith("Schemas.Integration", StringComparison.Ordinal)
            || FleetSchemas.Contains(value.Trim('"'), StringComparer.OrdinalIgnoreCase);
    }

    private static bool Mentions(string source, IReadOnlySet<string>? helpers) =>
        helpers?.Any(helper => Regex.IsMatch(source, $@"\b{helper}\b")) == true;

    private static bool IsTheRuleItself(string path) =>
        path.EndsWith("IntegrationIsolationRules.cs", StringComparison.Ordinal)
        || path.EndsWith("IntegrationIsolationTests.cs", StringComparison.Ordinal);

    private static bool IsGenerated(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal);
    }
}
