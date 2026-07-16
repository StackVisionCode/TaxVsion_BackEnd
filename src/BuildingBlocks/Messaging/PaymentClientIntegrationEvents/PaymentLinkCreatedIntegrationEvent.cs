namespace BuildingBlocks.Messaging.PaymentClientIntegrationEvents;

/// <summary>
/// Publicado por PaymentClient cuando el tenant genera un link mágico de cobro. Incluye
/// <see cref="Token"/> a propósito — Notification arma el email al taxpayer con la URL de
/// checkout (<c>/payments-client/checkout/{token}</c>); no es un secreto de plataforma, es
/// literalmente el contenido que se le envía al destinatario.
/// </summary>
public sealed record PaymentLinkCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentLinkId { get; init; }
    public Guid? TaxpayerId { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public required string Token { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
