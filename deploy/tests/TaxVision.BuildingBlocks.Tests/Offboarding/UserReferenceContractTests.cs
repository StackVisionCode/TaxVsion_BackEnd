using System.Text.RegularExpressions;
using BuildingBlocks.Domain;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Offboarding;

/// <summary>
/// Guardarraíl del retiro de empleados (offboard). El retiro es coreografía: Auth publica
/// <c>UserOffboardedIntegrationEvent</c> y cada servicio reacciona solo si alguien se acordó. Estos tests
/// leen el código fuente (sin referenciar los servicios) y exigen que (a) toda propiedad de dominio que
/// apunta a un usuario de staff declare qué le pasa en el retiro (<see cref="UserReferenceAttribute"/>, o
/// <c>/// @userReference(Kind, handledBy: x)</c> en Prisma), (b) quien la maneja exista y maneje el
/// retiro, y (c) retiro ⊇ suspensión. Ver DEVS/Futuras Implementaciones/Offboard-Revision-2026-11-03.md §6–§7.
/// <para>
/// Formato Prisma (Communication), en la línea de triple barra justo arriba del campo:
/// <c>/// @userReference(ActiveWork, handledBy: offboarding-consumers)</c> o
/// <c>/// @userReference(Provenance, note: "texto")</c>. <c>handledBy</c> = nombre de un archivo .ts (sin
/// extensión) o de una función/constante declarada en <c>src/</c>, cuyo archivo registra
/// <c>auth.user.offboarded.v1</c>.
/// </para>
/// <para>
/// Lo maneja el retiro de OTRO servicio por eventos (copias de datos de ese servicio): <c>HandledBy =
/// "Customer:CustomerUserOffboardedConsumer", Via = "CustomerAssignmentsChangedIntegrationEvent"</c> en .NET y
/// <c>handledBy: Customer:CustomerUserOffboardedConsumer, via: customer.assignments_changed.v1</c> en Prisma
/// (varios eventos con <c>|</c>). Se valida que el consumer exista en ESE servicio y maneje el retiro, que el
/// servicio local consuma cada evento de <c>Via</c> y, en .NET, que el servicio de origen lo nombre.
/// </para>
/// </summary>
public sealed class UserReferenceContractTests
{
    /// <summary>
    /// Servicios todavía sin anotar: se saltan en (a) y (b), NUNCA en (c). Esta lista solo se achica y debe
    /// quedar vacía: el test falla si un servicio de acá ya está anotado por completo.
    /// </summary>
    private static readonly string[] ServicesPendingAnnotation = [];

    /// <summary>Falsos positivos del patrón de nombres ("Servicio:Clase.Propiedad" → por qué no es un usuario).</summary>
    private static readonly Dictionary<string, string> NotUserReferences = new()
    {
        ["Tasks:FormFileScanVerdict.OwnerId"] = "dueño del archivo en CloudStorage: Customer o FormParticipant",
        ["Growth:ReferralCode.OwnerId"] = "quien refiere: la oficina o el contribuyente, nunca un empleado",
    };

    // Nombres que apuntan a un usuario; se excluyen los que terminan en otra entidad (tenant, archivo, cliente…).
    private static readonly Regex UserReferenceName = new(
        @"^(?:\w*UserId|\w*By|\w*ByActorId|Actor(?:Admin)?Id"
            + @"|SenderId|(?:Owner|Assigned|Assignee|Responsible|Host|Organizer|Preparer|RunAs|Successor|Coordinator"
            + @"|Caller|Callee|Agent|Invitee|Author)\w*Id)$",
        RegexOptions.Compiled
    );

    private static readonly Regex NotUserName = new(
        @"(?:Tenant|File|Customer|PortalUser|Participant|Signer|Session|Version|Template|Message|Folder)Id$",
        RegexOptions.Compiled
    );

    // Atributos (y comentarios intercalados) justo antes de una propiedad Guid con cuerpo { get…; }.
    private static readonly Regex GuidProperty = new(
        @"(?<attrs>(?:\[[^\[\]]*\]\s*|//[^\n]*\n\s*)*)(?:(?:public|internal|protected|private)\s+)+"
            + @"(?:(?:required|virtual|override|new|sealed)\s+)*Guid\??\s+(?<name>[A-Z]\w*)\s*\{",
        RegexOptions.Compiled
    );

    private static readonly Regex TypeDeclaration = new(
        @"\b(?:class|record|struct|interface|enum)\s+(?<name>[A-Z]\w*)",
        RegexOptions.Compiled
    );

    private static readonly Regex CSharpAnnotation = new(
        @"\bUserReference(?:Attribute)?\s*\(\s*(?:UserReferenceKind\.)?(?<kind>\w+)(?<args>[^\]]*)\]",
        RegexOptions.Compiled
    );

    private static readonly Regex CSharpHandledBy = new(
        @"\bHandledBy\s*=\s*(?:nameof\(\s*(?:[\w.]+\.)?(?<h>\w+)\s*\)|""(?<h>[^""]*)"")",
        RegexOptions.Compiled
    );

    // Entidades persistidas fuera de Domain: hereda de una clase base, o aparece en DbSet/configuración de EF.
    private static readonly Regex EntityDeclaration = new(
        @"\bclass\s+(?<name>\w+)\s*:\s*(?:TenantEntity|BaseEntity|AggregateRoot)\b"
            + @"|(?:\bDbSet|\bIEntityTypeConfiguration|\.Entity)\s*<\s*(?:[\w.]+\.)?(?<name>\w+)\s*>",
        RegexOptions.Compiled
    );

    /// <summary>Pseudo-servicio de las entidades compartidas de src/BuildingBlocks (p. ej. CustomerVisibility).</summary>
    private const string Shared = "BuildingBlocks";

    private static readonly Regex PrismaModel = new(@"^model\s+(?<name>\w+)\s*\{", RegexOptions.Compiled);

    private static readonly Regex PrismaGuidField = new(
        @"^\s*(?<name>\w+)\s+String\??\s.*@db\.UniqueIdentifier",
        RegexOptions.Compiled
    );

    // @userReference(Kind, clave: valor, clave: "texto"…): handledBy, via y note, en cualquier orden.
    private static readonly Regex PrismaAnnotation = new(@"@userReference\(\s*(?<kind>\w+)", RegexOptions.Compiled);

    private static readonly Regex PrismaArgument = new(
        @"\G\s*,\s*(?<k>\w+)\s*:\s*(?:""(?<v>[^""]*)""|(?<v>[^,\s""()]+))",
        RegexOptions.Compiled
    );

    private static readonly Regex CSharpVia = new(
        @"\bVia\s*=\s*(?:nameof\(\s*(?:[\w.]+\.)?(?<v>\w+)\s*\)|""(?<v>[^""]*)"")",
        RegexOptions.Compiled
    );

    // Lo que un servicio consume (.NET: tipo del primer parámetro de Handle; Node: register('evento')) y los
    // eventos que nombra (para comprobar que el servicio de origen publica el derivado).
    private static readonly Regex HandledType = new(
        @"\b(?:Handle|HandleAsync|Consume|ConsumeAsync)\s*\(\s*(?:[\w.]+\.)?(?<t>\w+)\s+\w+",
        RegexOptions.Compiled
    );

    private static readonly Regex TsRegistration = new(@"\w\s*\(\s*['""`](?<n>[\w.]+)['""`]", RegexOptions.Compiled);

    private static readonly Regex EventName = new(@"\b[A-Z]\w*Event\b", RegexOptions.Compiled);

    private static readonly Regex HandlesOffboarded = HandleOf("UserOffboardedIntegrationEvent");

    // Quien crea el evento (Auth, al retirar) hace su parte en el mismo comando: no lo "recibe".
    private static readonly Regex PublishesOffboarded = new(
        @"\bnew\s+(?:[\w.]+\.)?UserOffboardedIntegrationEvent\b",
        RegexOptions.Compiled
    );
    private static readonly Regex HandlesDeactivated = HandleOf("UserDeactivatedIntegrationEvent");
    private static readonly Regex RegistersOffboarded = RegistrationOf("auth.user.offboarded.v1");
    private static readonly Regex RegistersDeactivated = RegistrationOf("auth.user.deactivated.v1");

    // ---------------------------------------------------------------- repo real

    [Fact]
    public void Every_staff_user_reference_in_domain_declares_what_offboarding_does_with_it()
    {
        var report = Evaluate(LoadRepository(), ServicesPendingAnnotation);

        Assert.True(
            report.Violations.Count == 0,
            "Referencias a usuarios sin clasificar o sin manejo en el retiro. Anotá la propiedad con "
                + "[UserReference(UserReferenceKind.X, HandledBy = \"<clase que maneja UserOffboardedIntegrationEvent>\")] "
                + "(Provenance no lleva HandledBy). Si un campo de trabajo activo no tiene manejo, el arreglo es "
                + "manejarlo en el retiro, no marcarlo Provenance:\n  "
                + string.Join("\n  ", report.Violations)
        );
    }

    [Fact]
    public void Pending_annotation_list_only_shrinks()
    {
        var report = Evaluate(LoadRepository(), ServicesPendingAnnotation);

        Assert.True(
            report.PendingListProblems.Count == 0,
            "ServicesPendingAnnotation desactualizada (sacá estos servicios de la lista):\n  "
                + string.Join("\n  ", report.PendingListProblems)
        );
    }

    [Fact]
    public void Every_service_that_handles_suspension_also_handles_offboarding()
    {
        var report = Evaluate(LoadRepository(), ServicesPendingAnnotation);

        Assert.True(
            report.SuspensionWithoutOffboarding.Count == 0,
            "Retiro ⊇ suspensión: estos servicios manejan UserDeactivated pero no UserOffboarded "
                + "(Auth al retirar publica SOLO UserOffboarded):\n  "
                + string.Join("\n  ", report.SuspensionWithoutOffboarding)
        );
    }

    // ---------------------------------------------------------------- casos sintéticos

    private const string Consumer = """
        public static class DemoUserOffboardedConsumer
        {
            public static Task Handle(
                UserOffboardedIntegrationEvent msg,
                CancellationToken ct
            ) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void Flags_a_new_user_property_without_attribute()
    {
        var report = Evaluate(
            [
                Domain(
                    "Demo",
                    "Thing.cs",
                    """
                    public sealed class Thing
                    {
                        public Guid? ReviewerUserId { get; private set; }

                        // [UserReference(UserReferenceKind.Provenance)]
                        public Guid ApprovedBy { get; private set; }
                    }
                    """
                ),
                Code("Demo", "DemoUserOffboardedConsumer.cs", Consumer),
            ],
            []
        );

        Assert.Equal(2, report.Violations.Count);
        Assert.Contains(report.Violations, v => v.Contains("Demo.Domain/Thing.cs: Thing.ReviewerUserId"));
        Assert.Contains(report.Violations, v => v.Contains("Thing.ApprovedBy") && v.Contains("falta [UserReference"));
    }

    [Fact]
    public void Ignores_ids_that_are_not_users_and_accepts_classified_properties()
    {
        var report = Evaluate(
            [
                Domain(
                    "Demo",
                    "Thing.cs",
                    """
                    public sealed class Thing
                    {
                        public Guid TenantId { get; private set; }
                        public Guid CustomerId { get; private set; }
                        public Guid? SubmittedByPortalUserId { get; private set; }
                        public Guid StaffRecipientUserId => CreatedByUserId;

                        [UserReference(UserReferenceKind.Provenance)]
                        public Guid CreatedByUserId { get; private set; }

                        /// <summary>Doc.</summary>
                        [UserReference(UserReferenceKind.ActiveWork, HandledBy = "DemoUserOffboardedConsumer", Note = "x")]
                        public Guid? AssigneeUserId { get; private set; }

                        [UserReference(UserReferenceKind.Projection, HandledBy = nameof(DemoUserOffboardedConsumer))]
                        /// <summary>Atributo arriba del doc.</summary>
                        public Guid UserId { get; private set; }
                    }
                    """
                ),
                Code("Demo", "DemoUserOffboardedConsumer.cs", Consumer),
            ],
            []
        );

        Assert.Empty(report.Violations);
    }

    [Fact]
    public void Flags_active_work_without_handled_by_or_with_a_handler_that_does_not_exist()
    {
        var report = Evaluate(
            [
                Domain(
                    "Demo",
                    "Thing.cs",
                    """
                    public sealed class Thing
                    {
                        [UserReference(UserReferenceKind.ActiveWork)]
                        public Guid OwnerUserId { get; private set; }

                        [UserReference(UserReferenceKind.Credential, HandledBy = "GhostConsumer")]
                        public Guid HostUserId { get; private set; }

                        [UserReference(UserReferenceKind.Private, HandledBy = nameof(NotAnOffboardingHandler))]
                        public Guid OrganizerUserId { get; private set; }
                    }
                    """
                ),
                Code("Demo", "NotAnOffboardingHandler.cs", "public static class NotAnOffboardingHandler { }"),
                Code("Other", "GhostConsumer.cs", Consumer.Replace("DemoUserOffboardedConsumer", "GhostConsumer")),
            ],
            []
        );

        Assert.Equal(3, report.Violations.Count);
        Assert.Contains(report.Violations, v => v.Contains("Thing.OwnerUserId") && v.Contains("falta HandledBy"));
        Assert.Contains(report.Violations, v => v.Contains("Thing.HostUserId") && v.Contains("no existe"));
        Assert.Contains(
            report.Violations,
            v => v.Contains("Thing.OrganizerUserId") && v.Contains("no maneja UserOffboardedIntegrationEvent")
        );
    }

    [Fact]
    public void The_class_that_originates_offboarding_counts_as_its_handler()
    {
        var report = Evaluate(
            [
                Domain(
                    "Demo",
                    "Login.cs",
                    """
                    public sealed class Login
                    {
                        [UserReference(UserReferenceKind.Credential, HandledBy = "OffboardUserHandler")]
                        public Guid OwnerUserId { get; private set; }

                        [UserReference(UserReferenceKind.Credential, HandledBy = "SomeOtherHandler")]
                        public Guid HostUserId { get; private set; }
                    }
                    """
                ),
                Code(
                    "Demo",
                    "OffboardUserHandler.cs",
                    "public static class OffboardUserHandler { void Run() => bus.PublishAsync(new UserOffboardedIntegrationEvent { }); }"
                ),
                Code("Demo", "SomeOtherHandler.cs", "public static class SomeOtherHandler { }"),
            ],
            []
        );

        var violation = Assert.Single(report.Violations);
        Assert.Contains("Login.HostUserId", violation);
    }

    [Fact]
    public void Flags_prisma_fields_without_annotation_or_with_a_handler_that_does_not_handle_offboarding()
    {
        var report = Evaluate(
            [
                Prisma(
                    "Node",
                    """
                    model Meeting {
                      Id         String @id @db.UniqueIdentifier
                      TenantId   String @db.UniqueIdentifier
                      /// @userReference(ActiveWork, handledBy: offboarding-consumers)
                      HostUserId String @db.UniqueIdentifier
                      /// Quién la creó.
                      /// @userReference(Provenance, note: "auditoría")
                      CreatedByUserId String @db.UniqueIdentifier
                      PinnedByUserId String? @db.UniqueIdentifier
                      /// @userReference(Private, handledBy: auth-consumers)
                      OwnerUserId String @db.UniqueIdentifier
                      SenderId String @db.UniqueIdentifier
                      ScopeId  String @db.UniqueIdentifier
                    }
                    """
                ),
                Ts("Node", "offboarding-consumers.ts", "register('auth.user.offboarded.v1', async (env) => {});"),
                Ts("Node", "auth-consumers.ts", "register('auth.user.deactivated.v1', async (env) => {});"),
            ],
            []
        );

        Assert.Equal(3, report.Violations.Count);
        Assert.Contains(report.Violations, v => v.Contains("Meeting.PinnedByUserId") && v.Contains("@userReference"));
        Assert.Contains(report.Violations, v => v.Contains("Meeting.OwnerUserId") && v.Contains("no maneja"));
        Assert.Contains(report.Violations, v => v.Contains("Meeting.SenderId"));
    }

    [Fact]
    public void Scans_persisted_entities_outside_domain_but_not_other_classes()
    {
        var report = Evaluate(
            [
                Code(
                    "Demo",
                    "SeatSlot.cs",
                    """
                    public sealed class SeatSlot { public Guid UserId { get; private set; } }
                    public sealed record AssignSeatCommand { public Guid UserId { get; init; } }
                    """
                ),
                Code("Demo", "DemoDbContext.cs", "public DbSet<Infra.SeatSlot> Slots => Set<Infra.SeatSlot>();"),
                Code(
                    "Demo",
                    "LogConfiguration.cs",
                    """
                    public sealed class AuditLog { public Guid ActorUserId { get; set; } }
                    public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog> { }
                    """
                ),
            ],
            []
        );

        Assert.Equal(2, report.Violations.Count);
        Assert.Contains(report.Violations, v => v.Contains("SeatSlot.UserId"));
        Assert.Contains(report.Violations, v => v.Contains("AuditLog.ActorUserId"));
    }

    private const string AssignmentsConsumer = """
        public static class AssignmentsConsumer
        {
            public static Task Handle(AssignmentsChangedEvent evt, CancellationToken ct) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void Shared_entities_count_when_a_service_persists_them_and_may_name_another_services_handler()
    {
        var report = Evaluate(
            [
                SharedFile(
                    "Projection.cs",
                    """
                    public sealed class AssignmentProjection : TenantEntity
                    {
                        [UserReference(
                            UserReferenceKind.Projection,
                            HandledBy = "Customer:DemoUserOffboardedConsumer",
                            Via = "AssignmentsChangedEvent"
                        )]
                        public Guid UserId { get; private set; }
                    }
                    public sealed class Unpersisted { public Guid UserId { get; private set; } }
                    public sealed class Ghost : TenantEntity
                    {
                        [UserReference(
                            UserReferenceKind.Projection,
                            HandledBy = "Tasks:DemoUserOffboardedConsumer",
                            Via = "AssignmentsChangedEvent"
                        )]
                        public Guid OwnerUserId { get; private set; }
                    }
                    """
                ),
                SharedFile("AssignmentsConsumer.cs", AssignmentsConsumer),
                Code("Customer", "DemoUserOffboardedConsumer.cs", Consumer + "// new AssignmentsChangedEvent()"),
                Code(
                    "Tasks",
                    "TasksDbContext.cs",
                    "public DbSet<AssignmentProjection> A => Set<AssignmentProjection>();"
                ),
            ],
            []
        );

        // Solo Ghost: su consumer no existe en Tasks (y Tasks tampoco publica el evento derivado).
        Assert.All(report.Violations, v => Assert.Contains("Ghost.OwnerUserId", v));
        Assert.Contains(report.Violations, v => v.Contains("no existe en el servicio Tasks"));
        Assert.Contains(report.Violations, v => v.Contains("Tasks no publica 'AssignmentsChangedEvent'"));
    }

    [Fact]
    public void Handled_by_another_service_requires_a_derived_event_the_local_service_consumes()
    {
        var report = Evaluate(
            [
                Domain(
                    "Demo",
                    "Copies.cs",
                    """
                    public sealed class Copies
                    {
                        [UserReference(UserReferenceKind.Projection, HandledBy = "Customer:DemoUserOffboardedConsumer")]
                        public Guid PreparerUserId { get; private set; }

                        [UserReference(
                            UserReferenceKind.Projection,
                            HandledBy = "Customer:DemoUserOffboardedConsumer",
                            Via = "AssignmentsChangedEvent|PreparerUnassignedEvent"
                        )]
                        public Guid UserId { get; private set; }
                    }
                    """
                ),
                Code("Demo", "AssignmentsConsumer.cs", AssignmentsConsumer),
                Code("Customer", "DemoUserOffboardedConsumer.cs", Consumer + "// new AssignmentsChangedEvent()"),
                Prisma(
                    "Node",
                    """
                    model CustomerPreparerAssignment {
                      /// @userReference(Projection, handledBy: Customer:DemoUserOffboardedConsumer, via: customer.preparer_assigned.v1|customer.preparer_unassigned.v1, note: "Copia (de Customer)")
                      PreparerUserId String @db.UniqueIdentifier
                      /// @userReference(Projection, note: "x", via: customer.gone.v1, handledBy: Customer:DemoUserOffboardedConsumer)
                      UserId String @db.UniqueIdentifier
                    }
                    """
                ),
                Ts(
                    "Node",
                    "customer-consumers.ts",
                    "register('customer.preparer_assigned.v1', h); register('customer.preparer_unassigned.v1', h);"
                ),
            ],
            []
        );

        Assert.Equal(4, report.Violations.Count);
        Assert.Contains(report.Violations, v => v.Contains("Copies.PreparerUserId") && v.Contains("falta Via"));
        Assert.Contains(
            report.Violations,
            v => v.Contains("Copies.UserId") && v.Contains("Demo no consume 'PreparerUnassignedEvent'")
        );
        Assert.Contains(
            report.Violations,
            v => v.Contains("Copies.UserId") && v.Contains("Customer no publica 'PreparerUnassignedEvent'")
        );
        Assert.Contains(
            report.Violations,
            v => v.Contains("CustomerPreparerAssignment.UserId") && v.Contains("Node no consume 'customer.gone.v1'")
        );
    }

    [Fact]
    public void Flags_a_service_that_handles_deactivated_without_offboarded()
    {
        const string deactivatedOnly = """
            public static class DirectoryConsumer
            {
                public static Task Handle(UserDeactivatedIntegrationEvent msg) => Task.CompletedTask;
            }
            """;
        var report = Evaluate(
            [
                Code("Lagging", "DirectoryConsumer.cs", deactivatedOnly),
                Code("Fine", "DirectoryConsumer.cs", deactivatedOnly),
                Code("Fine", "DemoUserOffboardedConsumer.cs", Consumer),
                Ts("NodeLagging", "auth-consumers.ts", "register('auth.user.deactivated.v1', async () => {});"),
                Ts("NodeFine", "auth-consumers.ts", "register(\"auth.user.deactivated.v1\", async () => {});"),
                Ts("NodeFine", "offboarding.ts", "register(\"auth.user.offboarded.v1\", async () => {});"),
            ],
            ["Lagging"]
        );

        // La lista de pendientes nunca exime de (c).
        Assert.Equal(new[] { "Lagging", "NodeLagging" }, report.SuspensionWithoutOffboarding.Order().ToArray());
    }

    [Fact]
    public void Flags_a_pending_service_that_is_already_fully_annotated_or_does_not_exist()
    {
        var report = Evaluate(
            [
                Domain(
                    "Done",
                    "Thing.cs",
                    "public class Thing { [UserReference(UserReferenceKind.Provenance)] public Guid CreatedBy { get; set; } }"
                ),
                Domain("Todo", "Thing.cs", "public class Thing { public Guid CreatedBy { get; set; } }"),
            ],
            ["Done", "Todo", "Gone"]
        );

        Assert.Empty(report.Violations);
        Assert.Equal(2, report.PendingListProblems.Count);
        Assert.Contains(report.PendingListProblems, p => p.StartsWith("Done:"));
        Assert.Contains(report.PendingListProblems, p => p.StartsWith("Gone:"));
    }

    // ---------------------------------------------------------------- motor

    private sealed record SourceFile(string Service, string Path, bool IsDomain, string Text);

    private sealed record UserRef(
        string Service,
        string Path,
        string Type,
        string Property,
        bool IsPrisma,
        string? Kind,
        string? HandledBy,
        string? Via
    );

    /// <summary>Por servicio: nombre → ¿maneja el retiro?, eventos que consume y eventos que nombra.</summary>
    private sealed record ServiceIndex(
        Dictionary<string, bool> Handlers,
        HashSet<string> Consumed,
        HashSet<string> Mentioned
    );

    private sealed record Report(
        List<string> Violations,
        List<string> PendingListProblems,
        List<string> SuspensionWithoutOffboarding
    );

    private static Report Evaluate(IReadOnlyList<SourceFile> files, IReadOnlyCollection<string> pending)
    {
        var entities = EntityNamesByService(files);
        var refs = files
            .Where(f => f.Path.EndsWith(".cs"))
            .SelectMany(f => ScanCSharp(f, entities.GetValueOrDefault(f.Service) ?? []))
            .Concat(files.Where(f => f.Path.EndsWith(".prisma")).SelectMany(ScanPrisma))
            .ToList();

        var handlers = files
            .GroupBy(f => f.Service)
            .ToDictionary(g => g.Key, g => IndexOf(g.ToList()), StringComparer.Ordinal);

        var problems = refs.GroupBy(r => r.Service)
            .ToDictionary(g => g.Key, g => g.SelectMany(r => ProblemsOf(r, handlers)).ToList());

        var violations = problems.Where(p => !pending.Contains(p.Key)).SelectMany(p => p.Value).Order().ToList();

        List<string> pendingProblems = [];
        foreach (var service in pending)
        {
            if (!handlers.ContainsKey(service))
                pendingProblems.Add($"{service}: no existe en src/Services");
            else if (problems.GetValueOrDefault(service) is not { Count: > 0 })
                pendingProblems.Add($"{service}: ya está anotado por completo");
        }

        var lagging = files
            .GroupBy(f => f.Service)
            .Where(g => g.Any(HandlesSuspension) && !g.Any(HandlesOffboarding))
            .Select(g => g.Key)
            .Order()
            .ToList();

        return new Report(violations, pendingProblems, lagging);
    }

    // Por servicio, los tipos persistidos que viven fuera de Domain. Las compartidas cuentan si algún servicio
    // las persiste.
    private static Dictionary<string, HashSet<string>> EntityNamesByService(IReadOnlyList<SourceFile> files)
    {
        var result = files
            .Where(f => f.Path.EndsWith(".cs"))
            .GroupBy(f => f.Service)
            .ToDictionary(
                g => g.Key,
                g =>
                    g.SelectMany(f => EntityDeclaration.Matches(f.Text))
                        .Select(m => m.Groups["name"].Value)
                        .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal
            );
        result[Shared] = result.Values.SelectMany(n => n).ToHashSet(StringComparer.Ordinal);
        return result;
    }

    private static IEnumerable<UserRef> ScanCSharp(SourceFile file, IReadOnlySet<string> entities)
    {
        var types = TypeDeclaration.Matches(file.Text);
        foreach (Match property in GuidProperty.Matches(file.Text))
        {
            var name = property.Groups["name"].Value;
            if (!IsUserReferenceName(name))
                continue;

            var type = types.LastOrDefault(t => t.Index < property.Index)?.Groups["name"].Value ?? "?";
            if (!file.IsDomain && !entities.Contains(type))
                continue;
            // Un atributo comentado no cuenta.
            var attrs = Regex.Replace(property.Groups["attrs"].Value, @"//[^\n]*", "");
            var annotation = CSharpAnnotation.Match(attrs);
            var args = annotation.Success ? annotation.Groups["args"].Value : "";
            var handledBy = CSharpHandledBy.Match(args);
            var via = CSharpVia.Match(args);
            yield return new UserRef(
                file.Service,
                file.Path,
                type,
                name,
                IsPrisma: false,
                annotation.Success ? annotation.Groups["kind"].Value : null,
                handledBy.Success ? handledBy.Groups["h"].Value : null,
                via.Success ? via.Groups["v"].Value : null
            );
        }
    }

    private static IEnumerable<UserRef> ScanPrisma(SourceFile file)
    {
        var lines = file.Text.Split('\n');
        var model = "?";
        for (var i = 0; i < lines.Length; i++)
        {
            var modelMatch = PrismaModel.Match(lines[i]);
            if (modelMatch.Success)
            {
                model = modelMatch.Groups["name"].Value;
                continue;
            }

            var field = PrismaGuidField.Match(lines[i]);
            if (!field.Success || !IsUserReferenceName(Pascal(field.Groups["name"].Value)))
                continue;

            // El comentario de triple barra inmediatamente arriba del campo.
            string? kind = null;
            Dictionary<string, string> args = [];
            for (var j = i - 1; j >= 0 && lines[j].TrimStart().StartsWith("///"); j--)
            {
                var candidate = PrismaAnnotation.Match(lines[j]);
                if (!candidate.Success)
                    continue;
                kind = candidate.Groups["kind"].Value;
                args = PrismaArgument
                    .Matches(lines[j], candidate.Index + candidate.Length)
                    .ToDictionary(a => a.Groups["k"].Value, a => a.Groups["v"].Value);
            }

            yield return new UserRef(
                file.Service,
                file.Path,
                model,
                field.Groups["name"].Value,
                IsPrisma: true,
                kind,
                args.GetValueOrDefault("handledBy"),
                args.GetValueOrDefault("via")
            );
        }
    }

    private static IEnumerable<string> ProblemsOf(UserRef r, IReadOnlyDictionary<string, ServiceIndex> handlers)
    {
        if (NotUserReferences.ContainsKey($"{r.Service}:{r.Type}.{r.Property}"))
            yield break;

        var where = $"{r.Path}: {r.Type}.{r.Property}";
        if (r.Kind is null)
        {
            yield return r.IsPrisma
                ? $"{where} — falta /// @userReference(Kind, handledBy: <archivo o función>)"
                : $"{where} — falta [UserReference(...)]";
            yield break;
        }

        if (!Enum.TryParse<UserReferenceKind>(r.Kind, out var kind))
        {
            yield return $"{where} — tipo desconocido '{r.Kind}' (válidos: {string.Join(", ", Enum.GetNames<UserReferenceKind>())})";
            yield break;
        }

        if (r.HandledBy is null)
        {
            if (kind != UserReferenceKind.Provenance)
                yield return $"{where} — {kind} y falta HandledBy";
            yield break;
        }

        // "Servicio:Clase" cuando el retiro lo hace otro servicio (cadena de eventos, p. ej. entidades compartidas).
        var qualified = r.HandledBy.Split(':', 2);
        var (service, handler) = qualified.Length == 2 ? (qualified[0], qualified[1]) : (r.Service, r.HandledBy);
        var known = handlers.GetValueOrDefault(service)?.Handlers ?? [];
        if (!known.TryGetValue(handler, out var handlesOffboarding))
            yield return $"{where} — HandledBy '{r.HandledBy}' no existe en el servicio {service}";
        else if (kind != UserReferenceKind.Provenance && !handlesOffboarding)
            yield return $"{where} — '{r.HandledBy}' no maneja UserOffboardedIntegrationEvent (auth.user.offboarded.v1)";

        if (qualified.Length == 2)
            foreach (var problem in DerivedEventProblems(r, where, service, handlers))
                yield return problem;
    }

    // Retiro de otro servicio: el local tiene que consumir el evento derivado que corrige la copia.
    private static IEnumerable<string> DerivedEventProblems(
        UserRef r,
        string where,
        string source,
        IReadOnlyDictionary<string, ServiceIndex> handlers
    )
    {
        if (string.IsNullOrWhiteSpace(r.Via))
        {
            yield return $"{where} — lo maneja {source} por eventos: falta Via (evento que {r.Service} consume)";
            yield break;
        }

        var local = handlers.GetValueOrDefault(r.Service);
        var origin = handlers.GetValueOrDefault(source);
        foreach (var evt in r.Via.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (local?.Consumed.Contains(evt) != true)
                yield return $"{where} — {r.Service} no consume '{evt}' (Via)";
            if (!evt.Contains('.') && origin?.Mentioned.Contains(evt) != true)
                yield return $"{where} — {source} no publica '{evt}' (Via)";
        }
    }

    // Nombre → ¿maneja el retiro? C#: clases (cuerpo con Handle(UserOffboarded…) o que lo origina). Node: archivo o función
    // declarada (el archivo registra auth.user.offboarded.v1).
    private static ServiceIndex IndexOf(IReadOnlyList<SourceFile> files)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        HashSet<string> consumed = new(StringComparer.Ordinal);
        HashSet<string> mentioned = new(StringComparer.Ordinal);
        void Add(string name, bool handles) => result[name] = result.GetValueOrDefault(name) || handles;

        foreach (var file in files.Where(f => f.Path.EndsWith(".cs")))
        {
            consumed.UnionWith(HandledType.Matches(file.Text).Select(m => m.Groups["t"].Value));
            mentioned.UnionWith(EventName.Matches(file.Text).Select(m => m.Value));
            var types = TypeDeclaration.Matches(file.Text);
            for (var i = 0; i < types.Count; i++)
            {
                var end = i + 1 < types.Count ? types[i + 1].Index : file.Text.Length;
                var body = file.Text[types[i].Index..end];
                Add(
                    types[i].Groups["name"].Value,
                    HandlesOffboarded.IsMatch(body) || PublishesOffboarded.IsMatch(body)
                );
            }
        }

        foreach (var file in files.Where(f => f.Path.EndsWith(".ts")))
        {
            var handles = RegistersOffboarded.IsMatch(file.Text);
            consumed.UnionWith(TsRegistration.Matches(file.Text).Select(m => m.Groups["n"].Value));
            Add(Path.GetFileNameWithoutExtension(file.Path), handles);
            foreach (Match fn in TsDeclaration.Matches(file.Text))
                Add(fn.Groups["name"].Value, handles);
        }

        return new ServiceIndex(result, consumed, mentioned);
    }

    private static readonly Regex TsDeclaration = new(
        @"\b(?:function\s+(?<name>\w+)|(?:const|let)\s+(?<name>\w+)\s*=)",
        RegexOptions.Compiled
    );

    private static bool HandlesSuspension(SourceFile f) =>
        f.Path.EndsWith(".cs")
            ? HandlesDeactivated.IsMatch(f.Text)
            : f.Path.EndsWith(".ts") && RegistersDeactivated.IsMatch(f.Text);

    private static bool HandlesOffboarding(SourceFile f) =>
        f.Path.EndsWith(".cs")
            ? HandlesOffboarded.IsMatch(f.Text)
            : f.Path.EndsWith(".ts") && RegistersOffboarded.IsMatch(f.Text);

    private static bool IsUserReferenceName(string name) =>
        UserReferenceName.IsMatch(name) && !NotUserName.IsMatch(name);

    private static string Pascal(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private static Regex HandleOf(string eventType) =>
        new(
            @"\b(?:Handle|HandleAsync|Consume|ConsumeAsync)\s*\(\s*(?:[\w.]+\.)?" + eventType + @"\b",
            RegexOptions.Compiled
        );

    // Primer argumento de una llamada (register('x', …), on("x", …)); una lista de nombres no cuenta.
    private static Regex RegistrationOf(string eventName) =>
        new(@"\w\s*\(\s*['""`]" + Regex.Escape(eventName) + @"['""`]", RegexOptions.Compiled);

    // ---------------------------------------------------------------- fuentes

    private static SourceFile Domain(string service, string file, string text) =>
        new(service, $"{service}/{service}.Domain/{file}", true, text);

    private static SourceFile Code(string service, string file, string text) =>
        new(service, $"{service}/{service}.Application/{file}", false, text);

    private static SourceFile SharedFile(string file, string text) =>
        new(Shared, $"{Shared}/Visibility/{file}", false, text);

    private static SourceFile Prisma(string service, string text) =>
        new(service, $"{service}/prisma/schema.prisma", false, text);

    private static SourceFile Ts(string service, string file, string text) =>
        new(service, $"{service}/src/{file}", false, text);

    private static IReadOnlyList<SourceFile> LoadRepository()
    {
        var services = Path.Combine(RepositoryRoot().FullName, "src", "Services");
        List<SourceFile> files = [];
        foreach (var serviceDir in Directory.EnumerateDirectories(services))
        {
            var service = Path.GetFileName(serviceDir);
            foreach (var file in Directory.EnumerateFiles(serviceDir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsGenerated(file))
                    continue;
                var relative = Path.GetRelativePath(services, file).Replace('\\', '/');
                var isDomain = relative.Split('/')[1].EndsWith("Domain", StringComparison.Ordinal);
                files.Add(new SourceFile(service, relative, isDomain, File.ReadAllText(file)));
            }

            var nodeSrc = Path.Combine(serviceDir, "src");
            if (Directory.Exists(nodeSrc))
                foreach (var file in Directory.EnumerateFiles(nodeSrc, "*.ts", SearchOption.AllDirectories))
                    files.Add(ReadAs(service, services, file));

            var schema = Path.Combine(serviceDir, "prisma", "schema.prisma");
            if (File.Exists(schema))
                files.Add(ReadAs(service, services, schema));
        }

        // Entidades compartidas: solo cuentan los tipos que algún servicio persiste (ver EntityNamesByService).
        var shared = Path.Combine(RepositoryRoot().FullName, "src", Shared);
        foreach (var file in Directory.EnumerateFiles(shared, "*.cs", SearchOption.AllDirectories))
            if (!IsGenerated(file))
                files.Add(ReadAs(Shared, Path.GetDirectoryName(shared)!, file));

        return files;
    }

    private static SourceFile ReadAs(string service, string root, string file) =>
        new(service, Path.GetRelativePath(root, file).Replace('\\', '/'), false, File.ReadAllText(file));

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
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }
}
