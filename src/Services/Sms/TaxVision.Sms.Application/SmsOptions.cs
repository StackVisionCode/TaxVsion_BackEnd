namespace TaxVision.Sms.Application;

/// <summary>Config del servicio (sección `Sms`). El proveedor es global en el MVP (no per-tenant).</summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Código del adapter por defecto (ej. "generic"/"fake"). Debe existir un adapter registrado.</summary>
    public string DefaultProvider { get; set; } = "fake";

    /// <summary>Tope de mensajes por request de lote.</summary>
    public int MaxBatchSize { get; set; } = 1000;
}
