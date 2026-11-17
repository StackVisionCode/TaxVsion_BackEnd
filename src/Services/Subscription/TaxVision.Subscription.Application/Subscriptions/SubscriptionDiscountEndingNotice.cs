using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.TimeZones;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions;

/// <summary>El aviso de que un descuento de N meses termina en 7 días, a las 9:00 de la oficina.</summary>
public static class SubscriptionDiscountEndingNotice
{
    public const int DaysBefore = 7;

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
            || discount.EndsAtUtc is not { } endsAtUtc
            || !OfficeNoticeTime.IsSendHour(nowUtc, officeZone)
            || OfficeNoticeTime.DaysUntil(endsAtUtc, nowUtc, officeZone) != DaysBefore
        )
            return null;

        return new SubscriptionDiscountEndingIntegrationEvent
        {
            TenantId = discount.TenantId,
            CorrelationId = correlationId,
            TenantSubscriptionId = discount.TenantSubscriptionId,
            DiscountId = discount.Id,
            EndsAtUtc = endsAtUtc,
            DaysUntilEnd = DaysBefore,
            Source = discount.Source.ToString(),
            PercentBasisPoints = discount.PercentBasisPoints,
            AmountCents = discount.AmountCents,
            Currency = discount.Currency,
        };
    }
}
