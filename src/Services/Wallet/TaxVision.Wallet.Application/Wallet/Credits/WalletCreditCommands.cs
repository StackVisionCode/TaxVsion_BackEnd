namespace TaxVision.Wallet.Application.Wallet.Credits;

/// <summary>
/// Acredita dinero al monedero de <paramref name="TenantId"/> por una referencia opaca de un consumidor (p.ej.
/// <c>referral-reward</c>). El Wallet no conoce al consumidor ni valida su negocio: confía en el llamador de servicio
/// (M2M) y garantiza solo la idempotencia por <c>(TenantId, ReferenceType, ReferenceId)</c>. El tenant beneficiario
/// viaja en el cuerpo porque es distinto del tenant del token del llamador.
/// </summary>
public sealed record CreditWalletCommand(
    Guid TenantId,
    string ReferenceType,
    Guid ReferenceId,
    long AmountMicros,
    string Currency,
    string? Reason
);

/// <summary>Revierte un crédito ya acreditado con el mismo <c>ReferenceType/ReferenceId</c> (clawback). Idempotente.</summary>
public sealed record ReverseWalletCreditCommand(
    Guid TenantId,
    string ReferenceType,
    Guid ReferenceId,
    long AmountMicros,
    string Currency,
    string? Reason
);

/// <summary>Resultado del crédito/reversión. <paramref name="WasReplay"/> = ya existía (reintento idempotente).</summary>
public sealed record WalletCreditView(
    Guid LedgerEntryId,
    string Movement,
    long AmountMicros,
    long PostedMicrosAfter,
    long AvailableMicrosAfter,
    string Currency,
    bool WasReplay
);
