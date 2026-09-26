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
    bool IsOffice
);
