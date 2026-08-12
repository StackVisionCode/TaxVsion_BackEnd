namespace TaxVision.Reminder.Domain.Reminders;

/// <summary>
/// <see cref="Missed"/> no es un lujo: sin él no se distingue «disparó» de «se perdió mientras el
/// servicio estuvo caído», y la métrica <c>reminder.misfired_total</c> no tendría de dónde salir.
/// </summary>
public enum ReminderStatus
{
    Scheduled = 1,
    Fired = 2,
    Snoozed = 3,

    /// <summary>Terminal — el usuario lo vio y lo cerró.</summary>
    Dismissed = 4,

    /// <summary>Terminal — lo canceló el usuario, o se cerró el objetivo al que apuntaba.</summary>
    Cancelled = 5,

    /// <summary>Terminal — la ventana de misfire pasó y se descartó en vez de avisar tarde.</summary>
    Missed = 6,
}
