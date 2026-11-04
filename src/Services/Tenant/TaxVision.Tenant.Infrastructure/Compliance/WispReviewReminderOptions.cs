namespace TaxVision.Tenant.Infrastructure.Compliance;

/// <summary>
/// Operación del job del WISP (sección <c>Tenant:WispReviewReminders</c>). Las reglas del aviso (12 meses,
/// 30 días) son del dominio; aquí solo cada cuánto corre, el tamaño del lote y el lock entre réplicas.
/// </summary>
public sealed class WispReviewReminderOptions
{
    public const string SectionName = "Tenant:WispReviewReminders";

    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuánto corre. Correr más de una vez al día no repite avisos (el agregado lo anota).</summary>
    public int IntervalMinutes { get; set; } = 1440;

    public int BatchSize { get; set; } = 100;

    /// <summary>Vida máxima del lock si la réplica que lo tiene muere a mitad de pasada.</summary>
    public int LockTtlMinutes { get; set; } = 30;

    public bool IsValid() =>
        IntervalMinutes is >= 1 and <= 1440 && BatchSize is >= 1 and <= 1000 && LockTtlMinutes is >= 1 and <= 1440;
}
