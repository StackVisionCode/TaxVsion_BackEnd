using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Lanzada por <see cref="IOutboundEmailProviderClient"/> ante cualquier fallo del envío, con la razón
/// ya normalizada (D3 §8) — nunca un <c>Result.Failure</c>, mismo criterio que <see cref="EmailProviderException"/>
/// del lado de lectura: el caller puede envolver la llamada en el circuit breaker Polly de Fase 10, que
/// solo cuenta fallos vía excepción.
/// </summary>
public sealed class OutboundEmailSendException(
    SendFailureReason reason,
    string message,
    Exception? innerException = null
) : Exception(message, innerException)
{
    public SendFailureReason Reason { get; } = reason;
}

/// <summary>
/// Interfaz hermana de <see cref="IEmailProviderClient"/> — deliberadamente separada, no agregada ahí.
/// <see cref="IEmailProviderClient"/> tiene un contrato explícito de solo-lectura (D3 §3.1); envío es
/// una responsabilidad distinta. <c>GmailApiClient</c>/<c>GraphApiClient</c> implementan ambas
/// interfaces sobre la misma instancia (reusan <c>HttpClient</c>/<c>IOAuthTokenManager</c>/
/// <c>ProviderCircuitBreakerRegistry</c> ya inyectados). <c>ImapClient</c> no la implementa — IMAP no
/// envía correo (eso es SMTP manual, D1 de Postmaster, fuera de alcance acá).
/// </summary>
public interface IOutboundEmailProviderClient
{
    ProviderCode ProviderCode { get; }

    /// <summary>
    /// Envía <paramref name="message"/> desde la cuenta <paramref name="accountId"/>. El caller (el
    /// handler, que ya cargó el <c>TenantEmailAccount</c> para verificar tenant) provee
    /// <paramref name="fromAddress"/>/<paramref name="fromDisplayName"/> — el client nunca resuelve la
    /// cuenta por su cuenta, solo sabe hablar con el proveedor. Lanza
    /// <see cref="OutboundEmailSendException"/> en cualquier fallo — nunca devuelve un resultado que
    /// represente un envío fallido.
    /// </summary>
    Task<SendMessageResult> SendMessageAsync(
        Guid accountId,
        string fromAddress,
        string? fromDisplayName,
        OutboundMessage message,
        CancellationToken ct = default
    );
}
