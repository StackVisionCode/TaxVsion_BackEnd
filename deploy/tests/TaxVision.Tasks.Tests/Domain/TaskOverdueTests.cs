using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class TaskOverdueTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Vencimiento = new(2026, 4, 15, 16, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// El barrido pasa cada hora y la tarea sigue vencida mañana: sin la marca, el asignado recibe el
    /// mismo aviso hasta silenciar el canal, y entonces deja de ver también los que sí importan.
    /// </summary>
    [Fact]
    public void The_overdue_notice_is_marked_only_once()
    {
        var task = NewTask();

        Assert.True(task.MarkOverdueNotified(Now));
        Assert.False(task.MarkOverdueNotified(Now.AddHours(1)));
    }

    [Fact]
    public void Moving_the_due_date_lets_it_warn_again()
    {
        var task = NewTask();
        task.MarkOverdueNotified(Now);

        // Aflojar una fecha legal exige motivo; sin él ChangeDue falla y la marca no se toca.
        var moved = task.ChangeDue(
            DueDate.Create(Now.AddDays(30), "America/New_York", true).Value,
            UserId,
            Now,
            "Prorroga del IRS"
        );

        Assert.True(moved.IsSuccess);
        Assert.Null(task.OverdueNotifiedAtUtc);
        Assert.True(task.MarkOverdueNotified(Now.AddDays(31)));
    }

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                Guid.NewGuid(),
                UserId,
                TaskTitle.Create("Transmitir el e-file").Value,
                null,
                TaskPriority.High,
                TaskReference.None,
                DueDate.Create(Vencimiento, "America/New_York", true).Value,
                null,
                UserId,
                Vencimiento.AddDays(-30)
            )
            .Value;
}
