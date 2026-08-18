namespace TaxVision.Reminder.Domain.ValueObjects;

/// <summary>
/// Qué clase de objeto se recuerda. <see cref="General"/> es lo que hace a Reminder usable sin que
/// existan Calendar ni Task: «recordame llamar a Pérez el jueves» no apunta a nada.
///
/// Reminder guarda el <c>TargetId</c> como ID opaco y <b>nunca</b> consulta al otro contexto para
/// validar que exista (eso reintroduciría el acoplamiento síncrono que ADR-R-01 prohíbe).
/// </summary>
public enum ReminderCategory
{
    General = 1,
    Calendar = 2,
    Task = 3,
    Note = 4,
}
