namespace TaxVision.Campaigns.Application.Scheduling;

/// <summary>Opciones del scheduler de campañas (<c>Campaigns:Scheduler</c>).</summary>
public sealed class CampaignSchedulerOptions
{
    public const string SectionName = "Campaigns:Scheduler";

    /// <summary>
    /// Autocuración: libera los agendados cuyo run activo ya terminó y pausa los que ya pasaron su horario
    /// (D-CMP-1). Apagado hasta correr la reposición única.
    /// </summary>
    public bool HealStuckSchedules { get; set; }

    /// <summary>Margen tras el cierre del run antes de tratar el agendado como atascado (el consumer lo libera antes).</summary>
    public TimeSpan HealGrace { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Si la audiencia o el Wallet no responden, se reintenta el slot hasta esta edad. Después se salta, salvo un
    /// OneTime sin Wallet, que queda pausado para que la oficina decida.
    /// </summary>
    public TimeSpan MaxFireDelay { get; set; } = TimeSpan.FromHours(6);
}
