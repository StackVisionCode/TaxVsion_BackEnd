namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>
/// Estado de negocio de una <see cref="TaskItem"/>. Los números son valores de persistencia: se
/// comparan por igualdad, nunca con <c>&lt;</c> o <c>&gt;</c>.
///
/// <para>
/// No hay <c>Blocked</c>: el bloqueo es <c>OpenBlockerCount &gt; 0</c> y es ortogonal al estado, así
/// que al desbloquearse la tarea sigue donde estaba sin tener que recordar a qué estado volver.
/// </para>
/// </summary>
public enum TaskItemStatus
{
    NotStarted = 1,
    InProgress = 2,

    /// <summary>Espera de negocio, no bloqueo: la tarea avanzó hasta donde podía y espera al cliente.</summary>
    WaitingOnClient = 3,

    /// <summary>Terminal, reversible con <c>Reopen()</c>.</summary>
    Completed = 4,

    /// <summary>Terminal, reversible con <c>Reopen()</c>.</summary>
    Cancelled = 5,
}
