namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Nunca incluye nada del token — ni cifrado (D3 §12.4).</summary>
public sealed record TenantEmailAccountDto(
    Guid Id,
    string EmailAddress,
    string ProviderCode,
    string? DisplayName,
    string Status,
    DateTime? ConnectedAtUtc,
    DateTime CreatedAtUtc
);
