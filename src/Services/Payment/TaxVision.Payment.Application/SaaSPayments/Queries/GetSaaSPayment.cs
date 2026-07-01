using BuildingBlocks.Results;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.SaaSPayments;

namespace TaxVision.Payment.Application.SaaSPayments.Queries;

public sealed record GetSaaSPaymentQuery(Guid PaymentId);

public sealed record SaaSPaymentDto(
    Guid Id,
    Guid TenantId,
    SaaSPaymentType PaymentType,
    PaymentStatus Status,
    long AmountCents,
    string Currency,
    string? StripePaymentIntentId,
    Guid ReferenceId,
    DateTime CreatedAtUtc,
    string? FailureReason);

public static class GetSaaSPaymentHandler
{
    public static async Task<Result<SaaSPaymentDto>> Handle(
        GetSaaSPaymentQuery query,
        ISaaSPaymentRepository payments,
        CancellationToken ct)
    {
        var payment = await payments.GetByIdAsync(query.PaymentId, ct);
        if (payment is null)
            return Result.Failure<SaaSPaymentDto>(
                new BuildingBlocks.Results.Error("Payment.NotFound", $"SaaS payment {query.PaymentId} not found."));

        return Result.Success(new SaaSPaymentDto(
            payment.Id,
            payment.TenantId,
            payment.PaymentType,
            payment.Status,
            payment.AmountCents,
            payment.Currency,
            payment.StripePaymentIntentId,
            payment.ReferenceId,
            payment.CreatedAtUtc,
            payment.FailureReason));
    }
}
