using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using Wolverine;

namespace TaxVision.Notification.Infrastructure.Sms;

/// <summary>
/// Puente real de SMS: en vez de loguear (el stub <see cref="LoggingSmsSender"/>), publica
/// <see cref="SmsSendRequestedIntegrationEvent"/> hacia el microservicio <c>Sms</c>, que resuelve
/// proveedor (Infobip), opt-out, idempotencia y failover. Notification no conoce al proveedor.
///
/// <para>
/// El contrato <see cref="ISmsSender.SendAsync"/> solo trae teléfono + texto, así que aquí se derivan
/// el resto de campos del evento: el <c>TenantId</c> del contexto ambiental (que el middleware de
/// tenant sella desde el evento entrante antes del consumer), un <c>CustomerId</c> determinístico por
/// SHA-256 del teléfono (destinatario externo, igual criterio que Campaigns) y una idempotencia estable
/// por (tenant|teléfono|texto) — dos envíos idénticos se deduplican; un OTP nuevo (texto distinto) sí sale.
/// </para>
/// </summary>
public sealed class IntegrationEventSmsSender(
    IMessageBus bus,
    ITenantContext tenant,
    ICorrelationContext correlation,
    ILogger<IntegrationEventSmsSender> logger
) : ISmsSender
{
    public async Task<Result> SendAsync(
        string phoneNumber,
        string text,
        CancellationToken ct,
        string? templateKey,
        string? idempotencyToken
    )
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return Result.Failure(new Error("Notification.Sms.NoDestination", "SMS destination phone is required."));

        if (!tenant.HasTenant)
        {
            logger.LogWarning("SMS bridge invoked without an active tenant context; message not published.");
            return Result.Failure(new Error("Notification.Sms.NoTenant", "No active tenant context for the SMS."));
        }

        var tenantId = tenant.TenantId;
        var trimmedPhone = phoneNumber.Trim();
        await bus.PublishAsync(
            new SmsSendRequestedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = correlation.CorrelationId,
                To = trimmedPhone,
                Body = text,
                CustomerId = DeterministicCustomerId(phoneNumber),
                IdempotencyKey = DeriveIdempotencyKey(tenantId, trimmedPhone, templateKey, idempotencyToken, text),
                SourceContext = string.IsNullOrWhiteSpace(templateKey)
                    ? "notification:sms"
                    : $"notification:sms:{templateKey}",
                TemplateKey = templateKey,
            }
        );

        return Result.Success();
    }

    /// <summary>Guid estable a partir del teléfono (el destinatario externo no tiene CustomerId real).</summary>
    private static Guid DeterministicCustomerId(string phoneNumber)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(phoneNumber.Trim()));
        return new Guid(hash.AsSpan(0, 16));
    }

    // Material estable de idempotencia: cuando el caller provee un token (p. ej. EventId del
    // hecho de origen), la clave no depende del texto y no puede usarse para fuerza-bruta sobre
    // el OTP guardado. Si no hay token, cae al patrón legacy por tenant|phone|text.
    private static string DeriveIdempotencyKey(
        Guid tenantId,
        string phoneNumber,
        string? templateKey,
        string? idempotencyToken,
        string text
    )
    {
        var material = string.IsNullOrWhiteSpace(idempotencyToken)
            ? $"{tenantId:N}|{phoneNumber}|{text}"
            : $"{tenantId:N}|{phoneNumber}|{templateKey ?? string.Empty}|{idempotencyToken}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
