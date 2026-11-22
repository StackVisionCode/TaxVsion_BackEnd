using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.ClientForms.Application.Participants.Prospects;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using static TaxVision.Tasks.Tests.Integration.Performance.DashboardPerformance;

namespace TaxVision.Tasks.Tests.Integration.Performance;

/// <summary>
/// Volumen del dashboard en Tasks: 50 000 tareas (1 de cada 10 subtarea) y 5 000 prospectos con su solicitud en un
/// tenant de prueba y tres de ruido con un tercio, en la base temporal BB-10. Mide el resumen, la búsqueda por
/// vencimiento (incluido statutory) y el conteo de prospectos con la oficina completa y con un empleado de 500
/// clientes asignados de 20 000.
/// </summary>
[Trait("Category", "Performance")]
public sealed class TasksDashboardPerformanceTests : IAsyncLifetime
{
    private const int Tasks = 50_000;
    private const int Prospects = 5_000;
    private const int Customers = 20_000;
    private const int Employees = 40;
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
    private static readonly ResolvedTimeZone Zone = ResolvedTimeZone.Resolve("America/New_York");
    private static readonly Guid[] Tenants = [.. AllTenants];
    private static readonly Guid Employee = Id("a5e5", 1, 0);
    private static readonly string ConnectionString = TasksIntegrationDatabase.ConnectionString;

    private static readonly string[] OwnTables =
    [
        "[dbo].[FormActionRuns]",
        "[dbo].[FormRequests]",
        "[dbo].[FormTemplateVersions]",
        "[dbo].[FormTemplates]",
        "[dbo].[FormParticipants]",
        "[dbo].[Tasks]",
        "[dbo].[CustomerAssignmentProjections]",
    ];

    public async Task InitializeAsync()
    {
        var (task, participant) = await TemplatesAsync();
        for (var t = 1; t <= Tenants.Length; t++)
            await SeedTenantAsync(t, task, participant, DivisorOf(t));
        await UpdateStatisticsAsync(ConnectionString, OwnTables);
    }

    public async Task DisposeAsync()
    {
        // Las subtareas primero: la madre tiene FK.
        await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new Microsoft.Data.SqlClient.SqlCommand(
                $"DELETE FROM [dbo].[Tasks] WHERE [ParentTaskId] IS NOT NULL AND [TenantId] IN ({string.Join(", ", Tenants.Select(x => $"'{x}'"))})",
                connection
            );
            command.CommandTimeout = TimeoutSeconds;
            await command.ExecuteNonQueryAsync();
        }
        await DeleteAsync(ConnectionString, Tenants, OwnTables);
        Assert.All((await CountAsync(ConnectionString, Tenants, OwnTables)).Values, count => Assert.Equal(0, count));
        Assert.All(
            (await CountAsync(FleetConnectionString(), Tenants, OwnTables)).Values,
            count => Assert.Equal(0, count)
        );
    }

    [Fact]
    public async Task Summary_due_search_and_prospects_with_volume()
    {
        Assert.Equal(Tasks + 1, (await CountAsync(ConnectionString, [Tenants[0]], "[dbo].[Tasks]"))["[dbo].[Tasks]"]);

        foreach (var (scope, canViewAll) in new[] { ("oficina", true), ("empleado 500/20 000", false) })
        {
            await MeasureAsync(
                "Tasks",
                "GET /tasks/summary",
                scope,
                ConnectionString,
                async capture =>
                {
                    await using var db = NewContext(capture);
                    var summary = await GetTaskSummaryHandler.Handle(
                        new GetTaskSummaryQuery(Tenants[0], Employee, canViewAll, null, null, null, Now),
                        new TaskSummaryReader(db),
                        new FixedZone(),
                        Visibility,
                        default
                    );
                    Assert.True(summary.Value.Open > 0);
                }
            );

            foreach (
                var due in new[]
                {
                    TaskDueBucket.Overdue,
                    TaskDueBucket.Today,
                    TaskDueBucket.Soon,
                    TaskDueBucket.Statutory,
                }
            )
                await MeasureAsync(
                    "Tasks",
                    $"GET /tasks/search?due={due.ToString().ToLowerInvariant()}&size=20",
                    scope,
                    ConnectionString,
                    async capture =>
                    {
                        await using var db = NewContext(capture);
                        var page = await SearchTasksHandler.Handle(
                            new SearchTasksQuery(
                                Tenants[0],
                                new TaskQueryFilter(),
                                1,
                                20,
                                Employee,
                                canViewAll,
                                due,
                                Now
                            ),
                            new TaskRepository(db),
                            new FixedZone(),
                            Visibility,
                            default
                        );
                        Assert.True(page.TotalCount > 0);
                    }
                );

            await MeasureAsync(
                "Tasks",
                "GET /tasks/forms/prospects/summary",
                scope,
                ConnectionString,
                async capture =>
                {
                    await using var db = NewContext(capture);
                    var counts = await CountProspectsByStatusHandler.Handle(
                        new CountProspectsByStatusQuery(Tenants[0], Employee, canViewAll),
                        ProspectReads(db),
                        default
                    );
                    Assert.NotNull(counts);
                }
            );
        }
    }

    private static readonly IOptions<TasksVisibilityOptions> Visibility = Options.Create(
        new TasksVisibilityOptions { Enabled = true }
    );

    // Una tarea y un prospecto reales: las copias heredan columnas válidas.
    private static async Task<(Guid Task, Guid Participant)> TemplatesAsync()
    {
        var task = TaskItem
            .Create(
                Tenants[0],
                Employee,
                TaskTitle.Create("Template").Value,
                null,
                TaskPriority.Normal,
                TaskReference.Create(Id("c057", 1, 0), null).Value,
                DueDate.EndOfCivilDay(new DateOnly(2026, 11, 21), Zone, false).Value,
                null,
                Employee,
                Now.AddDays(-20)
            )
            .Value;
        var participant = FormParticipant
            .Create(Tenants[0], "template@example.test", null, null, FormParticipantSource.DirectedRequest, Now)
            .Value;
        await using var db = NewContext(null, Tenants[0]);
        db.Tasks.Add(task);
        db.FormParticipants.Add(participant);
        await db.SaveChangesAsync();
        return (task.Id, participant.Id);
    }

    private static async Task SeedTenantAsync(int t, Guid taskTemplate, Guid participantTemplate, int divisor)
    {
        var tenant = $"'{Tenants[t - 1]}'";
        var user = (string n) => SqlId("a5e5", t, $"({n}) % {Employees}");
        var day = "DATEADD(DAY, (n % 121) - 60, CAST('2026-11-21' AS datetime2))";

        // Por n % 10: 3 sin empezar, 2 en curso, 1 esperando al cliente, 3 completadas y 1 cancelada. La n % 10 = 1
        // es subtarea de la anterior. Vencimiento de -60 a +60 días, la mitad con hora; 1 de cada 15 legal.
        await CloneAsync(
            ConnectionString,
            "[dbo].[Tasks]",
            $"[Id] = '{taskTemplate}'",
            Tasks / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = SqlId("7a5c", t, "n"),
                ["TenantId"] = tenant,
                ["Title"] = "CONCAT(N'Task ', n % 500)",
                ["Status"] =
                    "CASE WHEN n % 10 < 3 THEN 1 WHEN n % 10 < 5 THEN 2 WHEN n % 10 = 5 THEN 3 WHEN n % 10 < 9 THEN 4 ELSE 5 END",
                ["AssigneeUserId"] = $"CASE WHEN n % 5 = 0 THEN NULL ELSE {user("n / 3")} END",
                ["CreatedByUserId"] = user("n / 7"),
                ["CustomerId"] = $"CASE WHEN n % 8 = 0 THEN NULL ELSE {SqlId("c057", t, $"n % {Customers}")} END",
                ["ParentTaskId"] = $"CASE WHEN n % 10 = 1 THEN {SqlId("7a5c", t, "n - 1")} END",
                ["Depth"] = "CASE WHEN n % 10 = 1 THEN 1 ELSE 0 END",
                ["DueAtUtc"] = $"DATEADD(HOUR, (n % 2) * 15, {day})",
                ["DueIsStatutory"] = "CASE WHEN n % 15 = 0 THEN 1 ELSE 0 END",
                ["CompletedAtUtc"] =
                    "CASE WHEN n % 10 IN (6, 7, 8) THEN DATEADD(HOUR, -(n % 720), CAST('2026-11-21T15:00:00' AS datetime2)) END",
                ["CreatedAtUtc"] = "DATEADD(MINUTE, -n, CAST('2026-11-21T14:00:00' AS datetime2))",
            }
        );

        // Prospectos: 1 de cada 3 vinculado a un cliente; cada uno con una solicitud de un responsable.
        await CloneAsync(
            ConnectionString,
            "[dbo].[FormParticipants]",
            $"[Id] = '{participantTemplate}'",
            Prospects / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = SqlId("9a00", t, "n"),
                ["TenantId"] = tenant,
                ["Email"] = "CONCAT(N'p', n, N'@example.test')",
                ["LinkedCustomerId"] = $"CASE WHEN n % 3 = 0 THEN {SqlId("c057", t, $"n % {Customers}")} END",
                ["Status"] = "CASE WHEN n % 3 = 0 THEN 3 WHEN n % 3 = 1 THEN 2 ELSE 1 END",
            }
        );
        var formTemplate = SqlId("f070", t, "n");
        await InsertAsync(
            ConnectionString,
            "[dbo].[FormTemplates]",
            1,
            new Dictionary<string, string>
            {
                ["Id"] = formTemplate,
                ["TenantId"] = tenant,
                ["DraftDefinitionJson"] = "N'{}'",
            }
        );
        await InsertAsync(
            ConnectionString,
            "[dbo].[FormTemplateVersions]",
            1,
            new Dictionary<string, string>
            {
                ["Id"] = SqlId("f071", t, "n"),
                ["TenantId"] = tenant,
                ["FormTemplateId"] = formTemplate,
                ["ActionSetJson"] = "N'{}'",
                ["DefinitionJson"] = "N'{}'",
                ["MappingSetJson"] = "N'{}'",
            }
        );
        await InsertAsync(
            ConnectionString,
            "[dbo].[FormRequests]",
            Prospects / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = SqlId("f0a0", t, "n"),
                ["TenantId"] = tenant,
                ["FormTemplateId"] = SqlId("f070", t, "0"),
                ["FormTemplateVersionId"] = SqlId("f071", t, "0"),
                ["ParticipantId"] = SqlId("9a00", t, "n"),
                ["ParticipantKind"] = "2",
                ["CustomerLinkStatus"] = "0",
                ["ResponsibleUserId"] = user("n"),
                ["RequestedByUserId"] = user("n + 3"),
                ["Status"] = "1 + n % 3",
                ["CurrentRevisionNumber"] = "n % 2",
                ["LastActivityAtUtc"] = "DATEADD(MINUTE, -n, CAST('2026-11-21T14:00:00' AS datetime2))",
                ["RequestedAtUtc"] = "DATEADD(MINUTE, -n, CAST('2026-11-21T14:00:00' AS datetime2))",
            }
        );
        // 1 de cada 10 solicitudes intentó crear el cliente; la mitad dio duplicado.
        await InsertAsync(
            ConnectionString,
            "[dbo].[FormActionRuns]",
            Prospects / divisor / 10,
            new Dictionary<string, string>
            {
                ["Id"] = "NEWID()",
                ["TenantId"] = tenant,
                ["FormRequestId"] = SqlId("f0a0", t, "n * 10"),
                ["ActionType"] = "N'CreateCustomer'",
                ["ActionKey"] = "CONCAT(N'create-', n)",
                ["IdempotencyKey"] = "CONCAT(N'perf-', n)",
                ["Status"] = "4",
                ["ErrorCode"] = "CASE WHEN n % 2 = 0 THEN N'Customer.DuplicateFound' END",
                ["RequestedAtUtc"] = "CAST('2026-11-20T12:00:00' AS datetime2)",
            }
        );
        await InsertAsync(
            ConnectionString,
            "[dbo].[CustomerAssignmentProjections]",
            Customers / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = "NEWID()",
                ["TenantId"] = tenant,
                ["CustomerId"] = SqlId("c057", t, "n"),
                ["UserId"] = user("n / 50"),
                ["Version"] = "SYSUTCDATETIME()",
            }
        );
    }

    private static IProspectReadRepository ProspectReads(TasksDbContext db) =>
        (IProspectReadRepository)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(
                    "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.ProspectReadRepository",
                    throwOnError: true
                )!,
                db
            )!;

    private static string FleetConnectionString() =>
        Environment.GetEnvironmentVariable("TASKS_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("12c547c3-e9a6-462e-82c4-bc6f5b0d4e41")
            .Build()
            .GetConnectionString("Default")!;

    private static TasksDbContext NewContext(CommandCapture? capture, Guid tenant = default)
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(ConnectionString);
        if (capture is not null)
            options.AddInterceptors(capture);
        return new TasksDbContext(options.Options, new FixedTenant(tenant));
    }

    private sealed class FixedZone : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(Zone);
    }

    private sealed class FixedTenant(Guid tenant) : ITenantContext
    {
        public Guid TenantId => tenant;
        public bool HasTenant => tenant != Guid.Empty;

        public void SetTenant(Guid id) { }
    }
}
