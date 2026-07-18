using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.PaymentLinks;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Queries;

public static class SearchPaymentLinksHandler
{
    public static async Task<Result<IReadOnlyList<PaymentLinkResponse>>> Handle(
        SearchPaymentLinksQuery query,
        IPaymentLinkRepository links,
        CancellationToken ct
    )
    {
        var results = await links.SearchByTenantAsync(query.TenantId, query.Status, query.Page, query.PageSize, ct);

        return Result.Success<IReadOnlyList<PaymentLinkResponse>>(results.Select(Map).ToList());
    }

    private static PaymentLinkResponse Map(PaymentLink link) =>
        new(
            link.Id,
            link.TaxpayerId,
            link.Amount.AmountCents,
            link.Amount.Currency,
            link.Purpose.Kind.ToString(),
            link.Purpose.ExternalReferenceId,
            link.Token.Value,
            link.Status.ToString(),
            link.ExpiresAtUtc,
            link.CreatedAtUtc,
            link.UsedAtUtc,
            link.RelatedTenantPaymentId
        );
}
