namespace TaxVision.Sms.Application;

/// <summary>Config del servicio (sección `Sms`). El proveedor es global en el MVP (no per-tenant).</summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Código del adapter por defecto (ej. "generic"/"fake"). Debe existir un adapter registrado.</summary>
    public string DefaultProvider { get; set; } = "fake";

    /// <summary>
    /// Cadena de failover a nivel PLATAFORMA (decisión del SaaS, no del tenant). Lista priorizada de
    /// códigos de proveedor: se envía por el primero; si rechaza o está caído, se reintenta con el
    /// siguiente, y así. Vacía ⇒ se usa solo <see cref="DefaultProvider"/> (comportamiento clásico,
    /// sin failover). El endpoint de envío NO cambia — el ruteo es interno.
    /// </summary>
    public List<string> ProviderOrder { get; set; } = [];

    /// <summary>Tope de mensajes por request de lote.</summary>
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>Reconciliación de estado por pull (backstop de los DLR por webhook).</summary>
    public SmsReconciliationOptions Reconciliation { get; set; } = new();
}

/// <summary>Config del job de reconciliación de estado (sección `Sms:Reconciliation`).</summary>
public sealed class SmsReconciliationOptions
{
    /// <summary>Enciende el job de fondo. El endpoint manual (<c>POST /sms/messages/reconcile</c>) funciona
    /// aunque esté OFF.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuánto corre el job de fondo.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Antigüedad mínima desde el último cambio para considerar un mensaje atascado.</summary>
    public int MinAgeSeconds { get; set; } = 60;

    /// <summary>Tope de mensajes por corrida del job.</summary>
    public int BatchSize { get; set; } = 200;
}
