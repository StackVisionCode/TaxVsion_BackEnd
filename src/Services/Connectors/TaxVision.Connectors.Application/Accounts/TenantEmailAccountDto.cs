namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Nunca incluye nada del token — ni cifrado (D3 §12.4).</summary>
public sealed record TenantEmailAccountDto(
    Guid Id,
    string EmailAddress,
    string ProviderCode,
    string? DisplayName,
    string Status,
    DateTime? ConnectedAtUtc,
    DateTime CreatedAtUtc,
    // null = buzón de oficina (compartido); con valor = personal de ese usuario.
    Guid? OwnerUserId,
    bool IsOffice,
    // Permisos que el servicio pide hoy y esta conexión no aceptó: no vacío = "Update permissions".
    IReadOnlyList<string> MissingScopes,
    // Buzón de oficina ligado a alguien que se retiró: desconectarlo o volver a autorizarlo.
    bool NeedsReview = false,
    string? ReviewReason = null,
    DateTime? ReviewRequiredAtUtc = null
);
