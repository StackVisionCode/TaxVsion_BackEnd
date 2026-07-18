using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using Wolverine;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.ExpirePaymentLink;

public static class ExpirePaymentLinkHandler
{
    public static async Task<Result> Handle(
        ExpirePaymentLinkCommand command,
        IPaymentLinkRepository links,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var link = await links.GetByIdAsync(command.PaymentLinkId, command.TenantId, ct);
        if (link is null)
            return Result.Failure(new Error("PaymentLink.NotFound", "PaymentLink does not exist."));

        var nowUtc = DateTime.UtcNow;
        var expireResult = link.Expire(nowUtc);
        if (expireResult.IsFailure)
            return expireResult;

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(PaymentLink),
            link.Id,
            PaymentAuditAction.PaymentLinkExpired,
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: (object?)null,
            reason: null,
            nowUtc,
            ct
        );

        await bus.PublishAsync(
            new PaymentLinkExpiredIntegrationEvent
            {
                TenantId = command.TenantId,
                PaymentLinkId = link.Id,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
