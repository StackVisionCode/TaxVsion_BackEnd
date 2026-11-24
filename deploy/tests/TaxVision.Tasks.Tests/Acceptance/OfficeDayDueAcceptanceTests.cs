using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Acceptance;

/// <summary>
/// Aceptación de la Fase 4 (vencimientos civiles): una tarea con fecha 15-nov en una oficina de Nueva York vence,
/// avisa y pasa a Overdue recién después de las 23:59:59 locales del 15, y en todas partes se lee "15-nov".
/// </summary>
public sealed class OfficeDayDueAcceptanceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();
    private static readonly DateOnly November15 = new(2026, 11, 15);

    [Fact]
    public async Task A_task_due_on_the_15th_in_New_York_is_overdue_only_after_that_day_ends_there()
    {
        // Alta por la API con la fecha civil (como la manda el CRM desde el Lote 5).
        var tasks = new InMemoryTaskRepository();
        var bus = new FakeMessageBus();
        var created = await CreateTaskHandler.Handle(
            new CreateTaskCommand(
                TenantId,
                Creator,
                "File the 1040",
                null,
                TaskPriority.High,
                Preparer,
                null,
                null,
                DueAtUtc: null,
                DueTimeZoneId: null,
                DueIsStatutory: true,
                EstimatedHours: null,
                DueOn: November15
            ),
            tasks,
            new RecordingUnitOfWork(),
            new RecordingTaskMetrics(),
            new FixedOfficeZone("America/New_York"),
            new EveryCustomer(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, tasks);
        var task = Assert.Single(tasks.Stored);

        // Lo que leen CRM, portal y correos: la fecha civil.
        Assert.Equal(November15, created.Value.DueOn);
        Assert.Equal(November15, Assert.Single(bus.Published.OfType<TaskCreatedIntegrationEvent>()).DueOn);

        // Avisa a las 9:00 de Nueva York del mismo día (P32), no la noche anterior.
        var reminder = TaskDueReminder.RequestFor(task, Preparer, 0, "corr")!;
        var fireAtUtc = reminder.AnchorAtUtc!.Value.AddMinutes(-(reminder.LeadMinutes ?? 0));
        Assert.InRange(
            fireAtUtc,
            new DateTime(2026, 11, 15, 13, 59, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 15, 14, 0, 0, DateTimeKind.Utc)
        );

        // El barrido de vencidas (consulta real del repositorio) no la toma a las 23:59:58 locales y sí al terminar el día.
        await using var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        var repository = new TaskRepository(db);

        Assert.Empty(await repository.ListOverdueAsync(new DateTime(2026, 11, 16, 4, 59, 58, DateTimeKind.Utc), 10));
        Assert.Single(await repository.ListOverdueAsync(new DateTime(2026, 11, 16, 5, 0, 0, DateTimeKind.Utc), 10));
    }

    /// <summary>
    /// Por qué existe el Lote 7: lo guardado antes (medianoche UTC del 15) sigue venciendo a las 19:00 del 14 en
    /// Nueva York aunque las pantallas ya digan "15-nov" (regla de lectura P5). Solo migrar el instante lo corrige.
    /// </summary>
    [Fact]
    public async Task Old_data_saved_at_midnight_utc_still_falls_due_the_evening_before()
    {
        var task = TaskItem
            .Create(
                TenantId,
                Creator,
                TaskTitle.Create("File the 1040").Value,
                null,
                TaskPriority.High,
                TaskReference.None,
                DueDate.Create(new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc), "America/New_York", true).Value,
                null,
                Preparer,
                new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)
            )
            .Value;
        await using var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        Assert.Equal(November15, task.Due!.DueOn);
        // 14-nov 20:00 en Nueva York = 15-nov 01:00Z: ya figura vencida.
        Assert.Single(
            await new TaskRepository(db).ListOverdueAsync(new DateTime(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc), 10)
        );
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
