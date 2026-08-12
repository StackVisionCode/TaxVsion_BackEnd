namespace BuildingBlocks.Messaging.ReminderIntegrationEvents;

// ---------------------------------------------------------------------------
// Reminder Fase 7 (02_Contratos_Integracion_Y_Proyecciones.md §1) — 3 de entrada, 1 de salida.
//
// ADR-R-01: los eventos de ENTRADA los define Reminder, no Calendar ni Task. Un
// `using BuildingBlocks.Messaging.CalendarIntegrationEvents` dentro de Reminder es la señal de que
// se rompió esa regla: significaría que Reminder aprendió el vocabulario de otro contexto y ya no
// puede recibir peticiones de un cuarto servicio sin volver a tocarse.
//
// `Category` viaja como string, no como enum: un enum en el contrato acopla el versionado — sumar
// una categoría obligaría a redesplegar a todos los publicadores. Reminder parsea y descarta CON LOG
// (no con excepción) lo que no conoce.
// ---------------------------------------------------------------------------

/// <summary>
/// <c>reminder.requested.v1</c> — «quiero que me recuerden X en T». Lo publica cualquiera: Task,
/// Calendar, o el propio frontend por el endpoint HTTP. El publicador no conoce a Reminder, conoce
/// el contrato.
/// </summary>
public sealed record ReminderRequestedIntegrationEvent : IntegrationEvent
{
    /// <summary>Destinatario y solicitante: en v1 siempre la misma persona (los compartidos son non-goal).</summary>
    public required Guid UserId { get; init; }

    /// <summary><c>General</c> | <c>Calendar</c> | <c>Task</c> | <c>Note</c>.</summary>
    public required string Category { get; init; }

    /// <summary>Nulo solo si <see cref="Category"/> es <c>General</c>.</summary>
    public Guid? TargetId { get; init; }

    public required string Title { get; init; }
    public string? Body { get; init; }

    /// <summary>Zona IANA. Reminder no la infiere: Auth todavía no publica la del usuario.</summary>
    public required string TimeZoneId { get; init; }

    /// <summary>Modo anclado, junto con <see cref="LeadMinutes"/> (ADR-R-03).</summary>
    public DateTime? AnchorAtUtc { get; init; }

    /// <summary>Modo anclado, junto con <see cref="AnchorAtUtc"/>.</summary>
    public int? LeadMinutes { get; init; }

    /// <summary>Modo absoluto. Excluyente con el par anclado.</summary>
    public DateTime? FireAtUtc { get; init; }

    /// <summary>
    /// Clave estable de idempotencia (ADR-R-07). <b>Obligatoria</b>: sin ella, un reintento del bus
    /// crea un recordatorio duplicado y el usuario recibe el mismo aviso dos veces. Formato
    /// sugerido: <c>{origen}:{targetId:N}:{userId:N}</c>.
    /// </summary>
    public required string RequestKey { get; init; }
}

/// <summary>
/// <c>reminder.target_moved.v1</c> — «el objetivo cambió de fecha». Solo mueve a los recordatorios
/// <b>anclados</b> a él; los absolutos lo ignoran por diseño (invariante R6), y eso es un éxito, no
/// un error.
/// </summary>
public sealed record ReminderTargetMovedIntegrationEvent : IntegrationEvent
{
    public required string Category { get; init; }
    public required Guid TargetId { get; init; }
    public required DateTime NewAnchorAtUtc { get; init; }
}

/// <summary>
/// <c>reminder.target_closed.v1</c> — «el objetivo se completó, se canceló o se borró». Cancela
/// todos sus recordatorios pendientes. Sin este evento se siguen recordando tareas ya hechas, que
/// es la queja número uno de cualquier sistema de recordatorios.
/// </summary>
public sealed record ReminderTargetClosedIntegrationEvent : IntegrationEvent
{
    public required string Category { get; init; }
    public required Guid TargetId { get; init; }

    /// <summary>Por qué se cerró el <b>objetivo</b>: <c>completed</c> | <c>cancelled</c> | <c>deleted</c>.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// <c>reminder.due.v1</c> — disparó. Reminder <b>no entrega</b> (ADR-R-02): publica el hecho y
/// Notification decide canal, preferencias y agrupación.
///
/// <para>
/// Lleva el contenido completo a propósito. Es una foto del instante del disparo: si el usuario
/// edita el título un segundo después, el aviso que ya salió describe lo que había cuando sonó, y
/// el consumidor no necesita llamar de vuelta a Reminder para renderizar.
/// </para>
/// </summary>
public sealed record ReminderDueIntegrationEvent : IntegrationEvent
{
    public required Guid ReminderId { get; init; }
    public required Guid UserId { get; init; }
    public required string Category { get; init; }
    public Guid? TargetId { get; init; }
    public required string Title { get; init; }
    public string? Body { get; init; }
    public required string TimeZoneId { get; init; }

    /// <summary>Presente solo si era anclado — habilita textos del tipo «tu cita de las 15:00…».</summary>
    public DateTime? AnchorAtUtc { get; init; }

    public required DateTime FiredAtUtc { get; init; }

    /// <summary>Para textos del tipo «(pospuesto 2 veces)».</summary>
    public required int SnoozeCount { get; init; }
}
