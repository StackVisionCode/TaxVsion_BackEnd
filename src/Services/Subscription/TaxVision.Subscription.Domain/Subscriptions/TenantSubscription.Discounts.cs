using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>El descuento de la suscripción: uno vigente a la vez, nunca se combinan.</summary>
public sealed partial class TenantSubscription
{
    private readonly List<SubscriptionDiscount> _discounts = [];

    public IReadOnlyCollection<SubscriptionDiscount> Discounts => _discounts;

    public SubscriptionDiscount? ActiveDiscount(DateTime nowUtc) =>
        _discounts.FirstOrDefault(discount => discount.IsActiveAt(nowUtc));

    /// <summary>Pega un descuento. Si ya hay uno vigente, solo se reemplaza con <paramref name="replace"/>.</summary>
    public Result<SubscriptionDiscount> GrantDiscount(
        SubscriptionDiscountTerms terms,
        bool replace,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (ActiveDiscount(nowUtc) is { } current)
        {
            if (!replace)
                return Result.Failure<SubscriptionDiscount>(
                    new Error("Subscription.Discount.AlreadyActive", "The subscription already has an active discount.")
                );

            current.End("Replaced by a new discount.", nowUtc);
        }

        var discount = SubscriptionDiscount.Create(Id, TenantId, terms, actorUserId, nowUtc);
        _discounts.Add(discount);
        Touch(actorUserId, nowUtc);
        return Result.Success(discount);
    }

    public Result EndDiscount(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Subscription.Discount.ReasonRequired", "A reason is required."));

        if (ActiveDiscount(nowUtc) is not { } current)
            return Result.Failure(new Error("Subscription.Discount.NotActive", "There is no active discount."));

        current.End(reason, nowUtc);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Cierra los descuentos cuya ventana ya terminó; true si cerró alguno.</summary>
    public bool CloseExpiredDiscounts(DateTime nowUtc)
    {
        var expired = _discounts
            .Where(discount => discount.EndedAtUtc is null && discount.EndsAtUtc is { } end && end <= nowUtc)
            .ToList();
        foreach (var discount in expired)
            discount.End("Expired.", discount.EndsAtUtc!.Value);
        return expired.Count > 0;
    }

    /// <summary>Deja la marca del aviso de fin; falla si el descuento no existe, ya terminó o ya se avisó.</summary>
    public Result MarkDiscountEndingNoticeSent(Guid discountId, DateTime nowUtc)
    {
        if (_discounts.FirstOrDefault(discount => discount.Id == discountId) is not { } discount)
            return Result.Failure(new Error("Subscription.Discount.NotFound", "The discount does not exist."));

        if (!discount.IsActiveAt(nowUtc))
            return Result.Failure(new Error("Subscription.Discount.NotActive", "The discount is no longer active."));

        if (discount.EndingNoticeSentAtUtc is not null)
            return Result.Failure(
                new Error("Subscription.Discount.EndingNoticeAlreadySent", "The ending notice was already sent.")
            );

        discount.MarkEndingNoticeSent(nowUtc);
        return Result.Success();
    }

    /// <summary>El descuento que lleva un cobro, de dónde salió y cómo se lee en el recibo; (0, null, null) si no aplica.</summary>
    /// <param name="chargePlanCode">Plan del cobro; por defecto, el de la suscripción.</param>
    /// <param name="nextCharge">Cobro del cambio de plan: usa el código de un solo cobro pendiente.</param>
    public (long Cents, DiscountSource? Source, string? Label) DiscountWithSourceFor(
        DiscountChargeKind kind,
        long grossCents,
        string currency,
        DateTime chargeDateUtc,
        string? chargePlanCode = null,
        bool nextCharge = false
    )
    {
        foreach (var discount in _discounts)
        {
            var cents = discount.AmountFor(
                kind,
                grossCents,
                currency,
                chargeDateUtc,
                chargePlanCode ?? PlanCode,
                nextCharge
            );
            if (cents > 0)
                return (cents, discount.Source, discount.ReceiptLabel());
        }

        return (0, null, null);
    }

    /// <summary>Descuento (centavos) que lleva un cobro de esta suscripción con esa fecha; 0 si no aplica.</summary>
    public long DiscountFor(
        DiscountChargeKind kind,
        long grossCents,
        string currency,
        DateTime chargeDateUtc,
        string? chargePlanCode = null,
        bool nextCharge = false
    ) =>
        _discounts.Sum(discount =>
            discount.AmountFor(kind, grossCents, currency, chargeDateUtc, chargePlanCode ?? PlanCode, nextCharge)
        );

    /// <summary>El cobro del cambio de plan usó el código de un solo cobro que esperaba la renovación: ya no queda.</summary>
    public void ConsumePendingOnceDiscount(DateTime nowUtc)
    {
        foreach (var discount in _discounts.Where(discount => discount.IsPendingOnce(nowUtc)).ToList())
            discount.End("Used on the plan change.", nowUtc);
    }

    /// <summary>Un descuento limitado a un plan termina si la suscripción pasa a otro plan.</summary>
    public void EndDiscountIfPlanNotCovered(string newPlanCode, DateTime nowUtc)
    {
        if (
            ActiveDiscount(nowUtc) is { PlanCode: { } planCode } current
            && !string.Equals(planCode, newPlanCode, StringComparison.OrdinalIgnoreCase)
        )
            current.End("The new plan is outside the discount's scope.", nowUtc);
    }
}
