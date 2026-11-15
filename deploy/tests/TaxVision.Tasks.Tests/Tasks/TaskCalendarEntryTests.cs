using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Tasks;

public sealed class TaskCalendarEntryTests
{
    /// <summary>El calendario pinta la tarea en su fecha civil: el instante (fin del día de la oficina) cae al día siguiente en UTC.</summary>
    [Fact]
    public void A_calendar_entry_carries_the_civil_due_date()
    {
        var due = DueDate
            .EndOfCivilDay(new DateOnly(2026, 11, 15), ResolvedTimeZone.Resolve("America/New_York"), false)
            .Value;
        var task = TaskItem
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TaskTitle.Create("Preparar 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                due,
                null,
                null,
                new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)
            )
            .Value;

        Assert.Equal(new DateOnly(2026, 11, 15), TaskCalendarEntry.From(task).DueOn);
    }
}
