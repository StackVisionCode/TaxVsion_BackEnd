using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Offboarding;

/// <summary>
/// Guardarraíl del retiro con acuse por servicio y del pre-flight descubrible. Auth espera un acuse por cada
/// paso de su registro (<c>OffboardingParticipants</c>) y expone ese registro como lista de pre-flights. Estos
/// tests leen el código fuente (sin referenciar los servicios) y exigen que (a) el registro liste exactamente
/// los consumers reales de <c>UserOffboardedIntegrationEvent</c> (.NET: clase; Node: archivo que registra
/// <c>auth.user.offboarded.v1</c>), (b) liste exactamente los <c>offboarding-impact</c> reales (ruta y si
/// acepta POST con email) y (c) todo servicio con referencias ActiveWork/Credential/Private tenga pre-flight.
/// Un consumer o pre-flight nuevo sin registrar = retiro que nunca completa o diálogo sin ese servicio.
/// </summary>
public sealed class OffboardingParticipantsContractTests
{
    private const string RegistryFile = "src/Services/Auth/Application/Offboarding/OffboardingParticipants.cs";

    /// <summary>
    /// Servicios con referencias ActiveWork/Credential/Private que NO necesitan pre-flight (servicio → por qué).
    /// Solo se achica: falla si un servicio de acá ya no tiene esas referencias o ya expone pre-flight.
    /// </summary>
    private static readonly Dictionary<string, string> ImpactNotRequired = new()
    {
        ["Auth"] = "es quien retira: hace su parte en el mismo comando y el diálogo vive en Auth",
        ["Notification"] = "dispositivos push y preferencias del retirado: se revocan sin decisión de la oficina",
        ["Reminder"] = "recordatorios privados del retirado: se cancelan; no hay nada que reasignar",
    };

    // ---------------------------------------------------------------- repo real

    [Fact]
    public void The_registry_lists_exactly_the_real_offboarding_consumers()
    {
        var report = Evaluate(LoadRepository(), ImpactNotRequired);

        Assert.True(
            report.StepMismatches.Count == 0,
            "OffboardingParticipants (Auth) no coincide con los consumers reales de UserOffboardedIntegrationEvent. "
                + "Un paso sin registrar nunca cuenta; uno registrado que no existe deja el retiro siempre Incomplete:\n  "
                + string.Join("\n  ", report.StepMismatches)
        );
    }

    [Fact]
    public void The_registry_lists_exactly_the_real_offboarding_impact_endpoints()
    {
        var report = Evaluate(LoadRepository(), ImpactNotRequired);

        Assert.True(
            report.ImpactMismatches.Count == 0,
            "OffboardingParticipants (Auth) no coincide con los offboarding-impact reales (ruta o POST con email):\n  "
                + string.Join("\n  ", report.ImpactMismatches)
        );
    }

    [Fact]
    public void Every_service_with_active_work_credentials_or_private_data_exposes_offboarding_impact()
    {
        var report = Evaluate(LoadRepository(), ImpactNotRequired);

        Assert.True(
            report.MissingImpact.Count == 0,
            "Servicios con [UserReference(ActiveWork|Credential|Private)] sin offboarding-impact en el registro "
                + "(exponelo y registralo, o justificá en ImpactNotRequired):\n  "
                + string.Join("\n  ", report.MissingImpact)
        );
    }

    // ---------------------------------------------------------------- casos sintéticos

    private const string Registry = """
        public static IReadOnlyList<OffboardingParticipant> All { get; } =
        [
            new("Auth", "Seats", ["SeatsConsumer"]),
            new(
                "Calendar",
                "Calendar",
                ["CalendarUserOffboardedConsumer", "UserLifecyclePermissionsProjectionConsumer"],
                "/calendar/offboarding-impact/{userId}"
            ),
            new("Node", "Chat", ["offboarding-consumers"], "/node/offboarding-impact/{userId}", AcceptsEmail: true),
        ];
        """;

    [Fact]
    public void Parses_the_registry_format()
    {
        var parsed = ParseRegistry(Registry);

        Assert.Equal(["Auth", "Calendar", "Node"], parsed.Keys.Order().ToArray());
        Assert.Equal(
            ["CalendarUserOffboardedConsumer", "UserLifecyclePermissionsProjectionConsumer"],
            parsed["Calendar"].Steps.Order().ToArray()
        );
        Assert.Equal("/calendar/offboarding-impact/{userId}", parsed["Calendar"].ImpactPath);
        Assert.False(parsed["Calendar"].AcceptsEmail);
        Assert.Null(parsed["Auth"].ImpactPath);
        Assert.True(parsed["Node"].AcceptsEmail);
    }

    [Fact]
    public void Flags_unregistered_consumers_and_registered_ghosts()
    {
        var report = Evaluate(
            [
                File(RegistryFile, Registry),
                File(
                    "src/Services/Auth/Application/SeatsConsumer.cs",
                    "public static class SeatsConsumer { public static Task Handle(UserOffboardedIntegrationEvent e) => Task.CompletedTask; }"
                ),
                File(
                    "src/Services/Calendar/App/Consumers.cs",
                    """
                    public static class CalendarUserOffboardedConsumer
                    {
                        public static Task Handle(
                            BuildingBlocks.Messaging.AuthIntegrationEvents.UserOffboardedIntegrationEvent msg
                        ) => Task.CompletedTask;
                    }
                    public static class NewOffboardedConsumer { public static void Consume(UserOffboardedIntegrationEvent m) { } }
                    public static class DeactivatedConsumer { public static void Handle(UserDeactivatedIntegrationEvent m) { } }
                    """
                ),
                File("src/Services/Node/src/app/offboarding-consumers.ts", "register('auth.user.offboarded.v1', h);"),
                File("src/Services/Node/tests/offboarding.test.ts", "register('auth.user.offboarded.v1', h);"),
            ],
            new Dictionary<string, string>()
        );

        Assert.Equal(2, report.StepMismatches.Count);
        Assert.Contains(
            report.StepMismatches,
            m => m.Contains("Calendar:NewOffboardedConsumer") && m.Contains("sin registrar")
        );
        Assert.Contains(
            report.StepMismatches,
            m => m.Contains("Calendar:UserLifecyclePermissionsProjectionConsumer") && m.Contains("no existe")
        );
    }

    [Fact]
    public void Flags_impact_endpoints_that_differ_from_the_registry()
    {
        var report = Evaluate(
            [
                File(RegistryFile, Registry),
                File(
                    "src/Services/Calendar/Api/AppointmentsController.cs",
                    """
                    [Route("calendar/appointments")]
                    public sealed class AppointmentsController
                    {
                        [HttpGet("/calendar/offboarding-impact/{userId:guid}")]
                        public Task<IActionResult> Impact(Guid userId) => null!;
                    }
                    """
                ),
                File(
                    "src/Services/Storage/Api/ShareLinksController.cs",
                    """
                    [Route("storage")]
                    public sealed class ShareLinksController
                    {
                        [HttpGet("offboarding-impact/{userId:guid}")]
                        public Task<IActionResult> Impact(Guid userId) => null!;

                        [HttpPost("offboarding-impact/{userId:guid}")]
                        public Task<IActionResult> ImpactByEmail(Guid userId) => null!;
                    }
                    """
                ),
                File(
                    "src/Services/Office/Api/OfficeImpactController.cs",
                    """
                    [Route("office/offboarding-impact")]
                    public sealed class OfficeImpactController
                    {
                        [HttpGet("{userId:guid}")]
                        public Task<IActionResult> Get(Guid userId) => null!;

                        [HttpGet("other/{id:guid}")]
                        public Task<IActionResult> Other(Guid id) => null!;
                    }
                    """
                ),
                File(
                    "src/Services/Node/src/routes/meetings.route.ts",
                    "app.get(\n    '/node/offboarding-impact/:userId',\n    { preHandler: [] },\n  );"
                ),
            ],
            new Dictionary<string, string>()
        );

        // Storage y Office no están registrados; Node está registrado con email pero solo tiene GET.
        Assert.Equal(3, report.ImpactMismatches.Count);
        Assert.Contains(
            report.ImpactMismatches,
            m => m.StartsWith("Office:") && m.Contains("/office/offboarding-impact/{userId}")
        );
        Assert.Contains(
            report.ImpactMismatches,
            m => m.StartsWith("Storage:") && m.Contains("/storage/offboarding-impact/{userId}") && m.Contains("POST")
        );
        Assert.Contains(report.ImpactMismatches, m => m.StartsWith("Node:") && m.Contains("acceptsEmail"));
    }

    [Fact]
    public void Requires_impact_for_active_references_unless_justified_and_keeps_the_exemptions_honest()
    {
        var report = Evaluate(
            [
                File(RegistryFile, Registry),
                File(
                    "src/Services/Tasks/Domain/Task.cs",
                    "public class Task { [UserReference(UserReferenceKind.ActiveWork, HandledBy = \"X\")] public Guid AssigneeUserId { get; private set; } }"
                ),
                File(
                    "src/Services/Reminder/Domain/Reminder.cs",
                    "public class Reminder { [UserReference(UserReferenceKind.Private, HandledBy = \"X\")] public Guid OwnerUserId { get; private set; } }"
                ),
                File(
                    "src/Services/Notes/Domain/Note.cs",
                    "public class Note { [UserReference(UserReferenceKind.Provenance)] public Guid CreatedBy { get; private set; } }"
                ),
                File(
                    "src/Services/Node/prisma/schema.prisma",
                    "model M {\n  /// @userReference(ActiveWork, handledBy: offboarding-consumers)\n  HostUserId String @db.UniqueIdentifier\n}"
                ),
            ],
            new Dictionary<string, string> { ["Reminder"] = "privado", ["Gone"] = "ya no existe" }
        );

        Assert.Equal(2, report.MissingImpact.Count);
        Assert.Contains(report.MissingImpact, m => m.StartsWith("Tasks:"));
        Assert.Contains(report.MissingImpact, m => m.StartsWith("Gone:") && m.Contains("ImpactNotRequired"));
    }

    // ---------------------------------------------------------------- motor

    private sealed record SourceFile(string Path, string Text);

    private sealed record Participant(HashSet<string> Steps, string? ImpactPath, bool AcceptsEmail);

    private sealed record Impact(string Path, bool AcceptsEmail);

    private sealed record Report(
        List<string> StepMismatches,
        List<string> ImpactMismatches,
        List<string> MissingImpact
    );

    private static readonly Regex RegistryEntry = new(
        @"new\(\s*""(?<service>\w+)""\s*,\s*""(?<module>[^""]*)""\s*,\s*\[(?<steps>[^\]]*)\](?<rest>[^()\[\]]*)\)",
        RegexOptions.Compiled
    );

    private static readonly Regex StringLiteral = new(@"""(?<v>[^""]*)""", RegexOptions.Compiled);

    private static readonly Regex HandlesOffboarded = new(
        @"\b(?:Handle|HandleAsync|Consume|ConsumeAsync)\s*\(\s*(?:[\w.]+\.)?UserOffboardedIntegrationEvent\b",
        RegexOptions.Compiled
    );

    private static readonly Regex TypeDeclaration = new(
        @"\b(?:class|record|struct|interface|enum)\s+(?<name>[A-Z]\w*)",
        RegexOptions.Compiled
    );

    private static readonly Regex RegistersOffboarded = new(
        @"\w\s*\(\s*['""`]auth\.user\.offboarded\.v1['""`]",
        RegexOptions.Compiled
    );

    private static readonly Regex ClassRoute = new(@"\[Route\(\s*""(?<route>[^""]*)""\s*\)\]", RegexOptions.Compiled);

    private static readonly Regex CSharpImpactAction = new(
        @"\[Http(?<verb>Get|Post)\(\s*""(?<template>[^""]*)""",
        RegexOptions.Compiled
    );

    private static readonly Regex TsImpactRoute = new(
        @"\.(?<verb>get|post)\(\s*['""`](?<path>/[\w/-]*offboarding-impact/:userId)['""`]",
        RegexOptions.Compiled
    );

    private static readonly Regex ActiveCSharpReference = new(
        @"\bUserReference(?:Attribute)?\s*\(\s*(?:UserReferenceKind\.)?(?:ActiveWork|Credential|Private)\b",
        RegexOptions.Compiled
    );

    private static readonly Regex ActivePrismaReference = new(
        @"@userReference\(\s*(?:ActiveWork|Credential|Private)\b",
        RegexOptions.Compiled
    );

    private static Report Evaluate(
        IReadOnlyList<SourceFile> files,
        IReadOnlyDictionary<string, string> impactNotRequired
    )
    {
        var registryFile = files.SingleOrDefault(f => f.Path == RegistryFile);
        Assert.True(registryFile is not null, $"No se encontró el registro {RegistryFile}.");
        var registry = ParseRegistry(registryFile!.Text);
        var code = files.Where(f => f.Path != RegistryFile && ServiceOf(f.Path) is not null).ToList();

        return new Report(
            StepMismatches(registry, code),
            ImpactMismatches(registry, code),
            MissingImpact(registry, code, impactNotRequired)
        );
    }

    private static Dictionary<string, Participant> ParseRegistry(string text)
    {
        var result = new Dictionary<string, Participant>(StringComparer.Ordinal);
        foreach (Match entry in RegistryEntry.Matches(text))
        {
            var steps = StringLiteral
                .Matches(entry.Groups["steps"].Value)
                .Select(m => m.Groups["v"].Value)
                .ToHashSet(StringComparer.Ordinal);
            var rest = entry.Groups["rest"].Value;
            var path = StringLiteral.Match(rest) is { Success: true } literal ? literal.Groups["v"].Value : null;
            var acceptsEmail = Regex.IsMatch(rest, @"AcceptsEmail\s*:\s*true");
            result[entry.Groups["service"].Value] = new Participant(steps, path, acceptsEmail);
        }

        Assert.True(
            result.Count > 0,
            "El registro no tiene entradas con el formato esperado (new(\"Servicio\", \"Módulo\", [...]...))."
        );
        return result;
    }

    private static List<string> StepMismatches(Dictionary<string, Participant> registry, List<SourceFile> code)
    {
        var real = new HashSet<(string Service, string Step)>();
        foreach (var file in code)
        {
            var service = ServiceOf(file.Path)!;
            if (file.Path.EndsWith(".cs"))
            {
                var types = TypeDeclaration.Matches(file.Text);
                for (var i = 0; i < types.Count; i++)
                {
                    var end = i + 1 < types.Count ? types[i + 1].Index : file.Text.Length;
                    if (HandlesOffboarded.IsMatch(file.Text[types[i].Index..end]))
                        real.Add((service, types[i].Groups["name"].Value));
                }
            }
            else if (file.Path.EndsWith(".ts") && IsNodeSource(file.Path) && RegistersOffboarded.IsMatch(file.Text))
                real.Add((service, Path.GetFileNameWithoutExtension(file.Path)));
        }

        var registered = registry.SelectMany(p => p.Value.Steps.Select(s => (Service: p.Key, Step: s))).ToHashSet();
        return real.Except(registered)
            .Select(s => $"{s.Service}:{s.Step} — consumer real sin registrar")
            .Concat(registered.Except(real).Select(s => $"{s.Service}:{s.Step} — registrado pero no existe"))
            .Order()
            .ToList();
    }

    private static List<string> ImpactMismatches(Dictionary<string, Participant> registry, List<SourceFile> code)
    {
        var real = RealImpacts(code);
        var problems = new List<string>();
        foreach (var (service, impact) in real)
        {
            var participant = registry.GetValueOrDefault(service);
            if (participant?.ImpactPath is null)
                problems.Add(
                    $"{service}: expone {impact.Path}{(impact.AcceptsEmail ? " (+POST)" : "")} y no está en el registro"
                );
            else if (participant.ImpactPath != impact.Path)
                problems.Add($"{service}: registro dice {participant.ImpactPath}, la ruta real es {impact.Path}");
            else if (participant.AcceptsEmail != impact.AcceptsEmail)
                problems.Add(
                    $"{service}: registro dice acceptsEmail={participant.AcceptsEmail}, real {impact.AcceptsEmail}"
                );
        }

        foreach (var (service, participant) in registry.Where(p => p.Value.ImpactPath is not null))
            if (!real.ContainsKey(service))
                problems.Add($"{service}: registrado con {participant.ImpactPath} pero no existe esa ruta");

        return problems.Order().ToList();
    }

    private static Dictionary<string, Impact> RealImpacts(List<SourceFile> code)
    {
        var verbs = new Dictionary<(string Service, string Path), HashSet<string>>();
        void Add(string service, string path, string verb)
        {
            var key = (service, path);
            if (!verbs.TryGetValue(key, out var set))
                verbs[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(verb);
        }

        foreach (var file in code)
        {
            var service = ServiceOf(file.Path)!;
            if (file.Path.EndsWith(".cs"))
            {
                foreach (Match action in CSharpImpactAction.Matches(file.Text))
                {
                    var template = action.Groups["template"].Value;
                    var classRoute = ClassRoute
                        .Matches(file.Text[..action.Index])
                        .LastOrDefault()
                        ?.Groups["route"]
                        .Value;
                    var path = template.StartsWith('/') ? template : $"/{classRoute?.Trim('/')}/{template}";
                    // La ruta de la clase puede ser la que dice offboarding-impact (Tenant).
                    var normalized = Normalize(path);
                    if (normalized.EndsWith("/offboarding-impact/{userId}", StringComparison.Ordinal))
                        Add(service, normalized, action.Groups["verb"].Value);
                }
            }
            else if (file.Path.EndsWith(".ts") && IsNodeSource(file.Path))
            {
                foreach (Match route in TsImpactRoute.Matches(file.Text))
                    Add(service, Normalize(route.Groups["path"].Value), route.Groups["verb"].Value);
            }
        }

        var result = new Dictionary<string, Impact>(StringComparer.Ordinal);
        foreach (var ((service, path), set) in verbs)
        {
            Assert.True(!result.ContainsKey(service), $"{service} expone más de un offboarding-impact.");
            result[service] = new Impact(path, set.Contains("post"));
        }
        return result;
    }

    private static List<string> MissingImpact(
        Dictionary<string, Participant> registry,
        List<SourceFile> code,
        IReadOnlyDictionary<string, string> impactNotRequired
    )
    {
        var withActive = code.Where(f =>
                f.Path.EndsWith(".cs")
                    ? ActiveCSharpReference.IsMatch(f.Text)
                    : f.Path.EndsWith(".prisma") && ActivePrismaReference.IsMatch(f.Text)
            )
            .Select(f => ServiceOf(f.Path)!)
            .ToHashSet(StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var service in withActive.Order())
        {
            var hasImpact = registry.GetValueOrDefault(service)?.ImpactPath is not null;
            if (!hasImpact && !impactNotRequired.ContainsKey(service))
                problems.Add(
                    $"{service}: tiene referencias ActiveWork/Credential/Private y no expone offboarding-impact"
                );
        }

        foreach (var service in impactNotRequired.Keys.Order())
        {
            if (!withActive.Contains(service))
                problems.Add(
                    $"{service}: ya no tiene referencias ActiveWork/Credential/Private; sacalo de ImpactNotRequired"
                );
            else if (registry.GetValueOrDefault(service)?.ImpactPath is not null)
                problems.Add($"{service}: ya expone offboarding-impact; sacalo de ImpactNotRequired");
        }

        return problems;
    }

    private static string Normalize(string path) =>
        Regex
            .Replace(Regex.Replace(path, @"\{userId(?::\w+)?\}", "{userId}"), ":userId", "{userId}")
            .Replace("//", "/");

    // src/Services/<Servicio>/...
    private static string? ServiceOf(string path)
    {
        var parts = path.Split('/');
        return parts.Length > 3 && parts[0] == "src" && parts[1] == "Services" ? parts[2] : null;
    }

    // Solo el código del servicio Node (src/Services/<X>/src), no sus tests.
    private static bool IsNodeSource(string path) => path.Split('/') is { Length: > 4 } parts && parts[3] == "src";

    private static SourceFile File(string path, string text) => new(path, text);

    private static IReadOnlyList<SourceFile> LoadRepository()
    {
        var root = RepositoryRoot().FullName;
        var services = Path.Combine(root, "src", "Services");
        var files = new List<SourceFile>();
        foreach (var serviceDir in Directory.EnumerateDirectories(services))
        {
            foreach (var file in Directory.EnumerateFiles(serviceDir, "*.cs", SearchOption.AllDirectories))
                if (!IsGenerated(file))
                    files.Add(Read(root, file));

            var nodeSrc = Path.Combine(serviceDir, "src");
            if (Directory.Exists(nodeSrc))
                foreach (var file in Directory.EnumerateFiles(nodeSrc, "*.ts", SearchOption.AllDirectories))
                    files.Add(Read(root, file));

            var schema = Path.Combine(serviceDir, "prisma", "schema.prisma");
            if (System.IO.File.Exists(schema))
                files.Add(Read(root, schema));
        }

        return files;
    }

    private static SourceFile Read(string root, string file) =>
        new(Path.GetRelativePath(root, file).Replace('\\', '/'), System.IO.File.ReadAllText(file));

    private static bool IsGenerated(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        return path.Contains($"{sep}obj{sep}")
            || path.Contains($"{sep}bin{sep}")
            || path.Contains($"{sep}Migrations{sep}")
            || path.Contains($"{sep}node_modules{sep}");
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }
}
