using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;
using TaxVision.Reminder.Infrastructure.Scheduling;
using TaxVision.Reminder.Tests.Reminders;

namespace TaxVision.Reminder.Tests.Scheduling;

/// <summary>
/// La decisión de misfire. Vive en el aggregate a propósito: dentro del job de Quartz haría falta
/// un scheduler y un <c>IJobExecutionContext</c> para probar una comparación de dos fechas.
/// </summary>
public sealed class ReminderSchedulingTests
{
    private static readonly DateTime FireAtUtc = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid UserId = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(60);

    [Fact]
    public void FireOrMiss_DentroDeLaGracia_Dispara()
    {
        var reminder = Build();

        var result = reminder.FireOrMiss(FireAtUtc.AddMinutes(59), Grace);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReminderStatus.Fired, reminder.Status);
    }

    [Fact]
    public void FireOrMiss_PasadaLaGracia_SeDescarta()
    {
        var reminder = Build();

        var result = reminder.FireOrMiss(FireAtUtc.AddMinutes(61), Grace);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReminderStatus.Missed, reminder.Status);
        Assert.NotNull(reminder.ResolvedAtUtc);
    }

    /// <summary>
    /// El borde exacto pertenece a <c>Fired</c>: la regla es «supera la ventana», no «la alcanza».
    /// Sin este test, invertir el operador pasaría desapercibido.
    /// </summary>
    [Fact]
    public void FireOrMiss_JustoEnElBorde_Dispara()
    {
        var reminder = Build();

        reminder.FireOrMiss(FireAtUtc.Add(Grace), Grace);

        Assert.Equal(ReminderStatus.Fired, reminder.Status);
    }

    /// <summary>
    /// Failover de cluster: el mismo trigger puede ejecutarse dos veces y el usuario no debe recibir
    /// el aviso dos veces.
    /// </summary>
    [Fact]
    public void FireOrMiss_DosVeces_EsIdempotente()
    {
        var reminder = Build();
        reminder.FireOrMiss(FireAtUtc, Grace);
        var firstFiredAt = reminder.FiredAtUtc;

        var result = reminder.FireOrMiss(FireAtUtc.AddMinutes(1), Grace);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReminderStatus.Fired, reminder.Status);
        Assert.Equal(firstFiredAt, reminder.FiredAtUtc);
    }

    /// <summary>
    /// Se canceló entre que Quartz eligió el trigger y la ejecución. El job lo trata como trigger
    /// sobrante, no como fallo — reintentar no arreglaría nada.
    /// </summary>
    [Fact]
    public void FireOrMiss_SobreUnEstadoTerminal_Falla()
    {
        var reminder = Build();
        reminder.Cancel("user_request", FireAtUtc.AddMinutes(-5));

        var result = reminder.FireOrMiss(FireAtUtc, Grace);

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.InvalidTransition", result.Error.Code);
        Assert.Equal(ReminderStatus.Cancelled, reminder.Status);
    }

    // REM-2: un pospuesto sin trigger vivo se vuelve a agendar en la pasada de reconciliación.
    [Fact]
    public async Task ReconciliationJob_SnoozedWithoutTrigger_SchedulesIt()
    {
        var reminder = Build();
        Assert.True(reminder.FireOrMiss(FireAtUtc, Grace).IsSuccess);
        Assert.True(reminder.Snooze(TimeSpan.FromMinutes(10), FireAtUtc).IsSuccess);
        var repository = new FakeReminderRepository();
        repository.Seed(reminder);
        var scheduler = new RecordingScheduler();
        using var provider = new ServiceCollection()
            .AddSingleton<IReminderRepository>(repository)
            .BuildServiceProvider();
        var job = new ReminderScheduleReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            scheduler,
            Options.Create(new ReminderSchedulingOptions()),
            NullLogger<ReminderScheduleReconciliationJob>.Instance
        );

        await job.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!scheduler.Scheduled.Contains(reminder.Id) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await job.StopAsync(CancellationToken.None);

        Assert.Equal([reminder.Id], scheduler.Scheduled);
    }

    private static ReminderAggregate Build() =>
        ReminderAggregate
            .Create(
                TenantId,
                UserId,
                ReminderSubject.Create("Llamar a Pérez", body: null).Value,
                ReminderTarget.Create(ReminderCategory.General, targetId: null).Value,
                ReminderSchedule.Absolute(FireAtUtc, FireAtUtc.AddDays(-1)).Value,
                ReminderTimeZone.Create("America/Santo_Domingo").Value,
                RequestKey.Create("test:scheduling:1").Value,
                FireAtUtc.AddDays(-1)
            )
            .Value;
}
