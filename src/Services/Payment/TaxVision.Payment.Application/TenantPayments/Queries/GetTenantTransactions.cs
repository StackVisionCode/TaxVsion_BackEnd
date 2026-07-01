using BuildingBlocks.Results;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.SaaSPayments;
using TaxVision.Payment.Domain.TenantPayments;

namespace TaxVision.Payment.Application.TenantPayments.Queries;

public sealed record GetTenantTransactionsQuery(Guid TenantId, int Page = 1, int Size = 20);

public sealed record TenantTransactionDto(
    Guid Id,
    Guid TenantId,
    Guid? CustomerId,
    TenantPaymentProvider Provider,
    PaymentStatus Status,
    long AmountCents,
    string Currency,
    string? ExternalTransactionId,
    string Description,
    DateTime CreatedAtUtc,
    string? FailureReason);

public static class GetTenantTransactionsHandler
{
    public static async Task<Result<List<TenantTransactionDto>>> Handle(
        GetTenantTransactionsQuery query,
        ITenantTransactionRepository transactions,
        CancellationToken ct)
    {
        var list = await transactions.GetByTenantAsync(query.TenantId, query.Page, query.Size, ct);
        var dtos = list.Select(t => new TenantTransactionDto(
            t.Id,
            t.TenantId,
            t.CustomerId,
            t.Provider,
            t.Status,
            t.AmountCents,
            t.Currency,
            t.ExternalTransactionId,
            t.Description,
            t.CreatedAtUtc,
            t.FailureReason)).ToList();
        return Result.Success(dtos);
    }
}
