using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Application.Reminders.Commands;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Tests.Reminders;

/// <summary>
/// Los dos comportamientos de la Fase 6 que no se ven en un E2E feliz: la carrera de idempotencia y
/// que un recordatorio ajeno responda <c>NotFound</c>.
///
/// <para>
/// Con fakes escritos a mano, no con una librería de mocking — el repo no tiene ninguna, y para tres
/// puertos de pocos métodos un fake es más legible que un setup encadenado.
/// </para>
/// </summary>
public sealed class ReminderCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid UserId = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");
    private static readonly Guid OtherUserId = Guid.Parse("9c3e77aa-2222-4333-8444-555566667777");

    [Fact]
    public async Task Create_ConLaMismaRequestKey_DevuelveElExistenteSinCrearOtro()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();

        var first = await Create(reminders, scheduler, metrics);
        var second = await Create(reminders, scheduler, metrics);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Single(reminders.Stored);
        Assert.Single(scheduler.Scheduled);

        // Fase 9: `scheduled_total` cuenta altas reales, y el reintento queda contado aparte. Si
        // `duplicate_suppressed_total` fuera 0 para siempre, la RequestKey estaría mal construida.
        Assert.Single(metrics.Scheduled);
        Assert.Equal([ReminderDuplicateResolutions.Lookup], metrics.DuplicatesSuppressed);
    }

    /// <summary>
    /// Dos peticiones simultáneas pasan la consulta previa a la vez; la segunda choca contra el
    /// índice único. Se atrapa <see cref="ConflictException"/> — <b>no</b> <c>DbUpdateException</c>:
    /// el DbContext ya tradujo el <c>SqlException</c> 2601/2627 y <c>ConflictException</c> no hereda
    /// de aquélla, así que atrapar la equivocada dejaría salir un 500.
    /// </summary>
    [Fact]
    public async Task Create_CuandoLaCarreraLaGanaOtro_DevuelveElGanadorNoUn500()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();
        var winner = (await Create(reminders, scheduler, metrics)).Value;

        // El fake simula la carrera: la consulta previa no lo ve, pero el índice único sí existe.
        reminders.HideFromLookupOnce = true;
        var loser = await Create(reminders, scheduler, metrics);

        Assert.True(loser.IsSuccess);
        Assert.Equal(winner.Id, loser.Value.Id);
        Assert.Single(reminders.Stored);
        Assert.Equal([ReminderDuplicateResolutions.UniqueIndexRace], metrics.DuplicatesSuppressed);
    }

    [Fact]
    public async Task Dismiss_DeUnRecordatorioAjeno_DevuelveNotFoundNoForbidden()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var created = (await Create(reminders, scheduler)).Value;

        var result = await DismissReminderHandler.Handle(
            new DismissReminderCommand(TenantId, OtherUserId, created.Id),
            reminders,
            scheduler,
            new NoOpUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.NotFound", result.Error.Code);
        Assert.Empty(scheduler.Unscheduled);
    }

    [Fact]
    public async Task Dismiss_DelDueno_DesagendaElTrigger()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var created = (await Create(reminders, scheduler)).Value;
        reminders.Stored[0].FireOrMiss(DateTime.UtcNow, TimeSpan.FromHours(1));

        var result = await DismissReminderHandler.Handle(
            new DismissReminderCommand(TenantId, UserId, created.Id),
            reminders,
            scheduler,
            new NoOpUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ReminderStatus.Dismissed, result.Value.Status);
        Assert.Single(scheduler.Unscheduled);
    }

    /// <summary>
    /// El endpoint acepta texto libre, así que la métrica lo colapsa a <c>other</c>: etiquetar con el
    /// valor crudo haría una serie nueva en Prometheus por cada frase que escriba un usuario. La
    /// razón completa se sigue guardando en la fila, que es donde soporte la necesita.
    /// </summary>
    [Fact]
    public async Task Cancel_ConRazonLibre_GuardaElTextoPeroEtiquetaLaMetricaComoOther()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();
        var created = (await Create(reminders, scheduler, metrics)).Value;

        var result = await CancelReminderHandler.Handle(
            new CancelReminderCommand(TenantId, UserId, created.Id, "ya no hace falta, lo resolvió Pérez"),
            reminders,
            scheduler,
            new NoOpUnitOfWork(),
            metrics,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("ya no hace falta, lo resolvió Pérez", reminders.Stored[0].CancellationReason);
        Assert.Equal([ReminderCancellationReasons.Other], metrics.Cancelled);
    }

    private static Task<Result<Application.Reminders.ReminderResponse>> Create(
        FakeReminderRepository reminders,
        RecordingScheduler scheduler,
        RecordingReminderMetrics? metrics = null
    ) =>
        CreateReminderHandler.Handle(
            new CreateReminderCommand(
                TenantId,
                UserId,
                "Llamar a Pérez",
                Body: null,
                ReminderCategory.General,
                TargetId: null,
                FireAtUtc: DateTime.UtcNow.AddHours(2),
                AnchorAtUtc: null,
                LeadMinutes: null,
                TimeZone: "America/Santo_Domingo",
                RequestKey: "test:create:1"
            ),
            reminders,
            scheduler,
            reminders.AsUnitOfWork(),
            metrics ?? new RecordingReminderMetrics(),
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );
}
