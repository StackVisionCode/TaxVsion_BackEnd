using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.PaymentLinks;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.RevokePaymentLink;

public static class RevokePaymentLinkHandler
{
    public static async Task<Result> Handle(
        RevokePaymentLinkCommand command,
        IPaymentLinkRepository links,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var link = await links.GetByIdAsync(command.PaymentLinkId, command.TenantId, ct);
        if (link is null)
            return Result.Failure(new Error("PaymentLink.NotFound", "PaymentLink does not exist."));

        var nowUtc = DateTime.UtcNow;
        var revokeResult = link.Revoke(command.Reason, nowUtc);
        if (revokeResult.IsFailure)
            return revokeResult;

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(PaymentLink),
            link.Id,
            PaymentAuditAction.PaymentLinkRevoked,
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: (object?)null,
            reason: command.Reason,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
