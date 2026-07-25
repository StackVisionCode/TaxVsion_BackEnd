namespace BuildingBlocks.ResourceAuthorization;

/// <summary>
/// RBAC Fase 4 (RBAC_Hardening_Plan.md) — feature flag del chequeo de resource ownership.
/// <see cref="Enabled"/> = false por default: sale apagado en todos los servicios, se activa
/// manualmente por config tras QA en staging (plan §4, riesgo "cambia semántica visible"). Es el
/// mismo objeto de config en los 3 servicios afectados (CloudStorage/Signature/Correspondence) —
/// un solo flag por servicio, no uno por endpoint (granularidad de activación real la da QA
/// probando cada endpoint antes de encender el flag del servicio, no una config por endpoint).
/// </summary>
public sealed class ResourceOwnershipOptions
{
    public const string SectionName = "Authorization:ResourceOwnership";

    public bool Enabled { get; set; }
}
