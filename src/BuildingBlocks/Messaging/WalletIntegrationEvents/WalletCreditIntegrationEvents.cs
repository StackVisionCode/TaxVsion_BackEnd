using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.WalletIntegrationEvents;

/// <summary>
/// Un servicio pide acreditar (o revertir) dinero en el monedero de <c>TenantId</c> (la base
/// <see cref="IntegrationEvent"/>: el tenant BENEFICIARIO). Vocabulario GENÉRICO: el Wallet no conoce a quien pide ni
/// su negocio; trabaja sobre una referencia opaca (<see cref="ReferenceType"/> + <see cref="ReferenceId"/>) y es
/// idempotente por ella, así que reentregar el evento nunca acredita dos veces. Responde con
/// <see cref="WalletCreditAppliedIntegrationEvent"/>. Reemplaza la llamada HTTP M2M servicio→Wallet.
/// </summary>
[MessageIdentity("wallet.credit.requested.v1")]
public sealed record WalletCreditRequestedIntegrationEvent : IntegrationEvent
{
    /// <summary>Tipo de referencia en minúsculas, definido por quien pide (p. ej. <c>referral-reward</c>).</summary>
    public required string ReferenceType { get; init; }

    public required Guid ReferenceId { get; init; }

    /// <summary>Monto en micros (1 USD = 1_000_000). Siempre positivo; <see cref="Reverse"/> decide el sentido.</summary>
    public required long AmountMicros { get; init; }

    public required string Currency { get; init; }
    public string? Reason { get; init; }

    /// <summary>false = acreditar; true = revertir un crédito previo con la misma referencia.</summary>
    public bool Reverse { get; init; }
}

/// <summary>
/// Resultado de una <see cref="WalletCreditRequestedIntegrationEvent"/>, con la misma referencia opaca para que quien
/// pidió la reconozca. Un fallo de negocio (moneda, saldo insuficiente, crédito inexistente…) viaja aquí con su código;
/// los fallos transitorios no se publican: el consumidor reintenta.
/// </summary>
[MessageIdentity("wallet.credit.applied.v1")]
public sealed record WalletCreditAppliedIntegrationEvent : IntegrationEvent
{
    public required string ReferenceType { get; init; }
    public required Guid ReferenceId { get; init; }
    public bool Reverse { get; init; }
    public required bool Succeeded { get; init; }
    public Guid? LedgerEntryId { get; init; }
    public string? ErrorCode { get; init; }

    /// <summary>true = ya estaba aplicado (reentrega idempotente).</summary>
    public bool WasReplay { get; init; }
}
