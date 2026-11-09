using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.ValueObjects;

namespace TaxVision.Sms.Application.Messages;

/// <summary>Ítem del lote ya validado, con su clave. <paramref name="StoredBody"/> es lo que se guarda: un cuerpo
/// sensible sale al proveedor completo y se persiste redactado.</summary>
public sealed record SmsOutgoingItem(
    PhoneE164 Phone,
    SmsBody Body,
    SmsBody StoredBody,
    IReadOnlyList<SmsMediaPayload> Media,
    string IdempotencyKey
)
{
    private static readonly long DerivedKeyWindowTicks = TimeSpan.FromMinutes(10).Ticks;

    /// <summary>Un ítem inválido falla solo: no aborta el lote.</summary>
    public static Result<SmsOutgoingItem> Prepare(SmsSendItemDto item, Guid tenantId, DateTime nowUtc)
    {
        if (item.CustomerId == Guid.Empty)
            return Result.Failure<SmsOutgoingItem>(SmsErrors.InvalidCustomer);

        var phone = PhoneE164.Create(item.To);
        if (phone.IsFailure)
            return Result.Failure<SmsOutgoingItem>(SmsErrors.InvalidDestination);

        var body = SmsBody.Create(item.Message);
        if (body.IsFailure)
            return Result.Failure<SmsOutgoingItem>(SmsErrors.InvalidBody);

        var storedBody = SmsBody.Create(SensitiveNotificationContent.ForStorage(item.TemplateKey, body.Value.Value));
        if (storedBody.IsFailure)
            return Result.Failure<SmsOutgoingItem>(storedBody.Error);

        var media = (item.Media ?? [])
            .Select(m => new SmsMediaPayload(m.Url, m.ContentType, m.FileName, m.SizeBytes))
            .ToList();

        var idempotencyKey = string.IsNullOrWhiteSpace(item.IdempotencyKey)
            ? DeriveIdempotencyKey(tenantId, item.CustomerId, phone.Value.Value, body.Value.Value, media, nowUtc)
            : item.IdempotencyKey.Trim();

        return Result.Success(new SmsOutgoingItem(phone.Value, body.Value, storedBody.Value, media, idempotencyKey));
    }

    /// <summary>Red de seguridad para llamadas M2M sin clave. Incluye una ventana de 10 min: un reenvío legítimo
    /// más tarde es otra clave. La media se canonicaliza (orden estable).</summary>
    private static string DeriveIdempotencyKey(
        Guid tenantId,
        Guid customerId,
        string to,
        string body,
        IReadOnlyList<SmsMediaPayload> media,
        DateTime nowUtc
    )
    {
        var mediaCanon = string.Join(
            "|",
            media.Select(m => $"{m.Url}:{m.ContentType}:{m.SizeBytes}").OrderBy(s => s, StringComparer.Ordinal)
        );
        var window = nowUtc.Ticks - nowUtc.Ticks % DerivedKeyWindowTicks;
        var canonical = $"{tenantId:N}|{customerId:N}|{to}|{body}|{mediaCanon}|{window}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "auto-" + Convert.ToHexStringLower(hash);
    }
}
