namespace BuildingBlocks.Messaging.WalletIntegrationEvents;

/// <summary>
/// Publicado por <c>TaxVision.Wallet</c> cada vez que cambia el saldo de un tenant (recarga acreditada,
/// reserva, consumo o liberación). Es una señal mínima (solo <c>TenantId</c>, en la base
/// <see cref="IntegrationEvent"/>): Communication lo relaya por socket a la sala staff del tenant para que
/// el front refresque saldo/historial en tiempo real. El front no confía en el payload para el monto —
/// re-consulta <c>GET /wallet</c>.
/// </summary>
public sealed record WalletBalanceChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>Motivo del cambio (topup | reserve | settle), informativo para logs/depuración.</summary>
    public string? Reason { get; init; }
}
