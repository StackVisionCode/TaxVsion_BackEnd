namespace TaxVision.Tasks.Application.ClientRequests;

/// <summary>
/// Recordatorios <b>al cliente</b> sobre lo que todavía no mandó. Nada que ver con los del
/// preparador, que son otro camino y siempre están activos.
/// </summary>
public sealed class ClientReminderOptions
{
    public const string SectionName = "Tasks:ClientReminders";

    /// <summary>
    /// Apagado por defecto, y a conciencia. Encenderlo sin coordinarlo es cómo el mismo cliente
    /// recibe tres correos el mismo día desde Task, Signature y Correspondence: el que decide
    /// activarlo tiene que saber qué más le está escribiendo ya.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Cadencia opuesta a la interna: al preparador se le avisa una hora antes, al cliente se le
    /// insiste con días de por medio. Recordarle cada hora que le falta un papel es acoso, no
    /// seguimiento.
    /// </summary>
    public int DaysBeforeDue { get; set; } = 3;
}
