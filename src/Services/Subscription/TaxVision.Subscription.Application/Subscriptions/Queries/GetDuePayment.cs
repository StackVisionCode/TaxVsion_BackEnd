using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.Renewals;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <summary>Lo vencido de la oficina para la pantalla "Pay what's due".</summary>
public sealed record GetDuePaymentQuery(Guid TenantId);

public sealed record DuePaymentItemView(
    string Kind,
    Guid Id,
    string Description,
    long GrossCents,
    long AmountCents,
    long DiscountCents,
    DateTime PeriodEndUtc
);

/// <summary><c>PlanRequired</c>: el plan está vencido y va siempre; el resto se puede desmarcar.</summary>
public sealed record DuePaymentView(
    bool PlanRequired,
    IReadOnlyList<DuePaymentItemView> Items,
    long TotalCents,
    long DiscountCents,
    string Currency,
    string? DiscountLabel
)
{
    public static DuePaymentView Empty { get; } = new(false, [], 0, 0, "USD", null);
}

public static class GetDuePaymentHandler
{
    public static async Task<Result<DuePaymentView>> Handle(
        GetDuePaymentQuery query,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        ISubscriptionSeatRepository seats,
        ITenantAddOnRepository addOns,
        IAddOnDefinitionRepository definitions,
        CancellationToken ct
    )
    {
        var read = await DueQuoteReader.ReadAsync(
            query.TenantId,
            selection: null,
            subscriptions,
            plans,
            seats,
            addOns,
            definitions,
            DateTime.UtcNow,
            ct
        );
        if (read.IsFailure)
            return read.Error.Code == "Subscription.Due.NothingDue"
                ? Result.Success(DuePaymentView.Empty)
                : Result.Failure<DuePaymentView>(read.Error);

        var quote = read.Value.Quote;
        return Result.Success(
            new DuePaymentView(
                quote.PlanRequired,
                [
                    .. quote.Items.Select(item => new DuePaymentItemView(
                        item.Kind,
                        item.TargetId,
                        item.Description,
                        item.GrossCents,
                        item.NetCents,
                        item.DiscountCents,
                        item.PeriodEndUtc
                    )),
                ],
                quote.TotalNetCents,
                quote.DiscountCents,
                quote.Currency,
                quote.DiscountLabel
            )
        );
    }
}
