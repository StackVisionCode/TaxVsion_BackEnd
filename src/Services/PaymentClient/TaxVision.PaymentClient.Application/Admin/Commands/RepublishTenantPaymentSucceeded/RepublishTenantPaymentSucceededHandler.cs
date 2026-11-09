using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPayments;
using Wolverine;

namespace TaxVision.PaymentClient.Application.Admin.Commands.RepublishTenantPaymentSucceeded;

public static class RepublishTenantPaymentSucceededHandler
{
    public const int MaxIds = 500;

    public static async Task<Result<RepublishTenantPaymentSucceededResponse>> Handle(
        RepublishTenantPaymentSucceededCommand command,
        ITenantPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var ids = command.TenantPaymentIds.Distinct().ToList();
        if (ids.Count == 0)
            return Result.Failure<RepublishTenantPaymentSucceededResponse>(
                new Error("Republish.NoIds", "At least one TenantPaymentId is required.")
            );
        if (ids.Count > MaxIds)
            return Result.Failure<RepublishTenantPaymentSucceededResponse>(
                new Error("Republish.TooManyIds", $"At most {MaxIds} TenantPaymentIds per call.")
            );

        var found = (await payments.GetByIdsAcrossTenantsAsync(ids, ct)).ToDictionary(p => p.Id);
        var eligible = new List<TenantPayment>();
        var skipped = new List<SkippedTenantPayment>();
        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var payment))
                skipped.Add(new SkippedTenantPayment(id, "NotFound"));
            else if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded)
                eligible.Add(payment);
            else
                skipped.Add(new SkippedTenantPayment(id, payment.Status.ToString()));
        }

        var republished = eligible.Select(p => p.Id).ToList();
        if (command.DryRun)
            return Result.Success(new RepublishTenantPaymentSucceededResponse(true, republished, skipped));

        var nowUtc = DateTime.UtcNow;
        foreach (var payment in eligible)
            await AuditEntryFactory.AppendAsync(
                audit,
                payment.TenantId,
                nameof(TenantPayment),
                payment.Id,
                PaymentAuditAction.TenantPaymentSignalRepublished,
                command.ActorUserId,
                correlation.CorrelationId,
                before: (object?)null,
                after: new { payment.Status, payment.PaidAtUtc },
                reason: "Owner-approved republish (Q-PCL-2).",
                nowUtc,
                ct
            );

        // Guardar la auditoría antes de publicar.
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var payment in eligible)
            await bus.PublishAsync(
                TenantPaymentSucceededSignal.Build(payment, payment.PaidAtUtc ?? nowUtc, correlation.CorrelationId)
            );

        return Result.Success(new RepublishTenantPaymentSucceededResponse(false, republished, skipped));
    }
}
