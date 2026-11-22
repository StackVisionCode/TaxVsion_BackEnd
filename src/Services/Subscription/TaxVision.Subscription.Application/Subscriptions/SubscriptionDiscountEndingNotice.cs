using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.TimeZones;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions;

/// <summary>
/// El aviso de que un descuento de N meses termina, desde 7 días antes y desde las 9:00 de la oficina. Sale una sola
/// vez: la marca del descuento lo impide aunque el job corra dos veces, y si se saltó el día sale en la siguiente
/// pasada con los días que falten.
/// </summary>
public static class SubscriptionDiscountEndingNotice
{
    public const int DaysBefore = 7;

    /// <summary>Si toca avisar, deja la marca en el descuento y devuelve el aviso; null si no toca.</summary>
    public static SubscriptionDiscountEndingIntegrationEvent? Take(
        TenantSubscription subscription,
        Guid discountId,
        DateTime nowUtc,
        ResolvedTimeZone officeZone,
        string correlationId
    )
    {
        if (
            subscription.Discounts.FirstOrDefault(discount => discount.Id == discountId) is not { } discount
            || For(discount, nowUtc, officeZone, correlationId) is not { } notice
        )
            return null;

        return subscription.MarkDiscountEndingNoticeSent(discountId, nowUtc).IsSuccess ? notice : null;
    }

    public static SubscriptionDiscountEndingIntegrationEvent? For(
        SubscriptionDiscount discount,
        DateTime nowUtc,
        ResolvedTimeZone officeZone,
        string correlationId
    )
    {
        // Solo los de N meses: "Once" es un cobro y "Forever" no termina.
        if (
            discount.Duration != DiscountDuration.Months
            || discount.EndedAtUtc is not null
            || discount.EndingNoticeSentAtUtc is not null
            || discount.EndsAtUtc is not { } endsAtUtc
            || endsAtUtc <= nowUtc
            || !OfficeNoticeTime.IsAtOrAfterSendHour(nowUtc, officeZone)
        )
            return null;

        var daysUntilEnd = OfficeNoticeTime.DaysUntil(endsAtUtc, nowUtc, officeZone);
        if (daysUntilEnd is < 1 or > DaysBefore)
            return null;

        return new SubscriptionDiscountEndingIntegrationEvent
        {
            TenantId = discount.TenantId,
            CorrelationId = correlationId,
            TenantSubscriptionId = discount.TenantSubscriptionId,
            DiscountId = discount.Id,
            EndsAtUtc = endsAtUtc,
            DaysUntilEnd = daysUntilEnd,
            Source = discount.Source.ToString(),
            PercentBasisPoints = discount.PercentBasisPoints,
            AmountCents = discount.AmountCents,
            Currency = discount.Currency,
        };
    }
}
