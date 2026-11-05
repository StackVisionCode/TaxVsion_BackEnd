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

    /// <summary>Si la audiencia no se puede resolver, se reintenta el slot hasta esta edad; después se salta.</summary>
    public TimeSpan MaxFireDelay { get; set; } = TimeSpan.FromHours(6);
}
