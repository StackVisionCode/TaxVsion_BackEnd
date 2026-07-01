using BuildingBlocks.Results;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.SaaSPayments;

namespace TaxVision.Payment.Application.SaaSPayments.Queries;

public sealed record GetSaaSPaymentsByTenantQuery(Guid TenantId);

public static class GetSaaSPaymentsByTenantHandler
{
    public static async Task<Result<List<SaaSPaymentDto>>> Handle(
        GetSaaSPaymentsByTenantQuery query,
        ISaaSPaymentRepository payments,
        CancellationToken ct)
    {
        var list = await payments.GetByTenantAsync(query.TenantId, ct);
        var dtos = list.Select(p => new SaaSPaymentDto(
            p.Id,
            p.TenantId,
            p.PaymentType,
            p.Status,
            p.AmountCents,
            p.Currency,
            p.StripePaymentIntentId,
            p.ReferenceId,
            p.CreatedAtUtc,
            p.FailureReason)).ToList();
        return Result.Success(dtos);
    }
}
