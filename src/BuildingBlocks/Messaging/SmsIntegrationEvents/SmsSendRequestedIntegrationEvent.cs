namespace BuildingBlocks.Messaging.SmsIntegrationEvents;

/// <summary>
/// Petición genérica de envío de UN SMS al microservicio Sms. Punto de entrada agnóstico del dominio:
/// cualquier caller (OTP de firma, invitación, entrega de documento/certificado, reminders…) publica
/// esto y el microservicio Sms lo ejecuta (opt-out, idempotencia, failover, proveedor), devolviendo los
/// <c>SmsMessage{Accepted,Delivered,Failed,Suppressed}</c> de resultado. El caller correlaciona por
/// <see cref="SourceContext"/>/<c>CorrelationId</c> sin que Sms conozca su dominio. TenantId/CorrelationId
/// vienen de <see cref="IntegrationEvent"/>.
/// </summary>
public sealed record SmsSendRequestedIntegrationEvent : IntegrationEvent
{
    /// <summary>Destino en formato E.164 (validación estricta en el servicio Sms).</summary>
    public required string To { get; init; }

    /// <summary>Cuerpo ya renderizado — el servicio Sms no tiene plantillas.</summary>
    public required string Body { get; init; }

    /// <summary>
    /// Identidad para opt-out/idempotencia. Para un destinatario externo sin CustomerId real, el caller
    /// deriva un Guid determinístico (SHA-256 del contacto), igual que Campaigns.
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>Clave de idempotencia por mensaje — el servicio Sms deduplica y no reenvía.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Opaco para Sms: solo correlación/observabilidad (p. ej. "signature:{requestId}:{signerId}:otp").</summary>
    public string? SourceContext { get; init; }

    /// <summary>Proveedor preferido (código); null = default/failover de la plataforma.</summary>
    public string? ProviderPreference { get; init; }
}
