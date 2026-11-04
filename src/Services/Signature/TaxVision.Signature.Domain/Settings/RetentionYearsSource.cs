namespace TaxVision.Signature.Domain.Settings;

/// <summary>De dónde salen los años de retención de una oficina (antes del mínimo del plan).</summary>
public enum RetentionYearsSource
{
    /// <summary>Los guardados en Signature (o el default de 7): la oficina no fijó su retención.</summary>
    Signature = 0,

    /// <summary>La retención de la oficina en Cumplimiento (Tenant).</summary>
    Office = 1,
}
