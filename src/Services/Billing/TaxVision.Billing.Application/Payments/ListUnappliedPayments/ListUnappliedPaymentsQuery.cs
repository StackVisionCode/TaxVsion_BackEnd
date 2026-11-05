using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Application.Payments.ListUnappliedPayments;

public sealed record ListUnappliedPaymentsQuery(Guid TenantId, bool IncludeResolved = false, int Take = 100);

public sealed record UnappliedPaymentResponse(
    Guid Id,
    Guid TenantPaymentId,
    Guid? InvoiceId,
    string ExternalReferenceId,
    long AmountCents,
    string Currency,
    string Reason,
    DateTime ReceivedAtUtc,
    DateTime? ResolvedAtUtc,
    string? Resolution,
    string? ResolutionNote,
    Guid? ResolvedByUserId
);

public static class ListUnappliedPaymentsHandler
{
    private const int MaxTake = 200;

    public static async Task<Result<IReadOnlyList<UnappliedPaymentResponse>>> Handle(
        ListUnappliedPaymentsQuery query,
        IUnappliedPaymentRepository unappliedPayments,
        CancellationToken ct
    )
    {
        var take = query.Take is > 0 and <= MaxTake ? query.Take : 100;
        var list = await unappliedPayments.ListByTenantAsync(query.TenantId, query.IncludeResolved, take, ct);

        IReadOnlyList<UnappliedPaymentResponse> response = list.Select(p => new UnappliedPaymentResponse(
                p.Id,
                p.TenantPaymentId,
                p.InvoiceId,
                p.ExternalReferenceId,
                p.AmountCents,
                p.Currency,
                p.Reason.ToString(),
                p.ReceivedAtUtc,
                p.ResolvedAtUtc,
                p.Resolution?.ToString(),
                p.ResolutionNote,
                p.ResolvedByUserId
            ))
            .ToList();

        return Result.Success(response);
    }
}
