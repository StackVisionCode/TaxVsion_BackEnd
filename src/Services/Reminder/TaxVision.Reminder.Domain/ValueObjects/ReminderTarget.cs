using BuildingBlocks.Results;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Domain.ValueObjects;

/// <summary>
/// Referencia polimórfica al objetivo, por ID opaco — mismo criterio que <c>NoteReference</c> en
/// Notes. Reminder guarda el ID; no sabe qué hay del otro lado ni lo valida contra nadie.
/// </summary>
public sealed record ReminderTarget
{
    private ReminderTarget(ReminderCategory category, Guid? targetId)
    {
        Category = category;
        TargetId = targetId;
    }

    public ReminderCategory Category { get; }
    public Guid? TargetId { get; }

    public static Result<ReminderTarget> Create(ReminderCategory category, Guid? targetId)
    {
        // T1 — General no apunta a nada. Si llega un targetId es que el publicador se equivocó de
        // categoría, y guardarlo dejaría un ID huérfano que nadie puede resolver.
        if (category == ReminderCategory.General && targetId is not null)
            return Result.Failure<ReminderTarget>(ReminderErrors.Target.UnexpectedTarget);

        // T2 — cualquier otra categoría necesita a qué apuntar; Guid.Empty no es un objetivo.
        if (category != ReminderCategory.General && (targetId is null || targetId == Guid.Empty))
            return Result.Failure<ReminderTarget>(ReminderErrors.Target.TargetRequired);

        return Result.Success(new ReminderTarget(category, targetId));
    }
}
