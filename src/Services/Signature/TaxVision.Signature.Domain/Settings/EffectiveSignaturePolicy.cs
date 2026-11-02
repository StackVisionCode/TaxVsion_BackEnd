namespace TaxVision.Signature.Domain.Settings;

/// <summary>
/// Lo que rige hoy para un tenant: los techos (plan + excepción) y, dentro de ellos, lo que eligió la oficina.
/// Si el plan baja, lo elegido se acota aquí al leerlo; no hace falta reescribir los ajustes guardados.
/// </summary>
/// <param name="Ceilings">Techos del plan con la excepción de PlatformAdmin.</param>
/// <param name="ForRequests">Lo que aplica el dominio al armar solicitudes: techos de cantidad y horas, tamaño y
/// páginas de la oficina y los canales de la oficina dentro de los del plan.</param>
/// <param name="DefaultTokenHours">Vida por defecto del enlace del firmante, dentro del techo.</param>
/// <param name="RetentionYears">Retención de la oficina, nunca por debajo del mínimo del plan.</param>
/// <param name="PurgeEnabled">La oficina activó la purga y el plan la permite.</param>
/// <param name="DefaultChannel">Canal preseleccionado, siempre dentro de los permitidos.</param>
public sealed record EffectiveSignaturePolicy(
    SignatureLimits Ceilings,
    SignatureLimits ForRequests,
    int DefaultTokenHours,
    int RetentionYears,
    bool PurgeEnabled,
    VerificationChannel DefaultChannel
);
