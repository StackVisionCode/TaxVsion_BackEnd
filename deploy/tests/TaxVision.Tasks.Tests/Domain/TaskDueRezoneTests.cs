using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Tasks.Tests.Domain;

/// <summary>
/// P9: la oficina cambió de zona. Un vencimiento abierto de fecha civil (fin del día en la zona anterior) pasa al fin
/// del mismo día en la zona nueva, sin avisar un "vencimiento cambiado". Uno con hora puesta o de otra zona no se toca.
/// </summary>
public sealed class TaskDueRezoneTests
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");
    private static readonly ResolvedTimeZone Chicago = ResolvedTimeZone.Resolve("America/Chicago");
    private static readonly DateOnly Day = new(2026, 11, 20);
    private static readonly DateTime Now = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_civil_due_moves_to_the_end_of_the_same_day_in_the_new_zone()
    {
        var task = NewTask(DueDate.EndOfCivilDay(Day, NewYork, isStatutory: true).Value);
        task.ClearDomainEvents();

        Assert.True(task.RezoneCivilDues("America/New_York", Chicago));

        Assert.Equal(LocalDay.EndUtc(Day, Chicago), task.Due!.DueAtUtc);
        Assert.Equal("America/Chicago", task.Due.TimeZoneId);
        Assert.True(task.Due.IsStatutory);
        Assert.Empty(task.DomainEvents.OfType<TaskDueChangedDomainEvent>());
    }

    [Fact]
    public void A_due_with_a_set_hour_or_from_another_zone_stays()
    {
        var atNoon = NewTask(
            DueDate.Create(new DateTime(2026, 11, 20, 17, 0, 0, DateTimeKind.Utc), "America/New_York", false).Value
        );
        var denver = NewTask(DueDate.EndOfCivilDay(Day, ResolvedTimeZone.Resolve("America/Denver"), false).Value);

        Assert.False(atNoon.RezoneCivilDues("America/New_York", Chicago));
        Assert.False(denver.RezoneCivilDues("America/New_York", Chicago));
    }

    [Fact]
    public void The_client_due_of_a_waiting_task_moves_too()
    {
        var task = NewTask(null);
        Assert.True(
            task.MoveToWaitingOnClient(
                ClientRequestNote.Create("W-2").Value,
                LocalDay.EndUtc(Day, NewYork),
                Guid.NewGuid(),
                Now,
                Day
            ).IsSuccess
        );

        Assert.True(task.RezoneCivilDues("America/New_York", Chicago));

        Assert.Equal(LocalDay.EndUtc(Day, Chicago), task.ClientDueAtUtc);
        Assert.Equal(Day, task.ClientDueOn);
    }

    private static TaskItem NewTask(DueDate? due) =>
        TaskItem
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TaskTitle.Create("Preparar 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                due,
                null,
                Guid.NewGuid(),
                Now
            )
            .Value;
}
