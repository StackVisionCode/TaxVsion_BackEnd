using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.CreatePaymentLink;

public static class CreatePaymentLinkHandler
{
    public static async Task<Result<CreatePaymentLinkResponse>> Handle(
        CreatePaymentLinkCommand command,
        IPaymentLinkRepository links,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var amountResult = Money.Create(command.AmountCents, command.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<CreatePaymentLinkResponse>(amountResult.Error);

        var purposeResult = PaymentPurpose.Create(command.PurposeKind, command.PurposeExternalReferenceId);
        if (purposeResult.IsFailure)
            return Result.Failure<CreatePaymentLinkResponse>(purposeResult.Error);

        var nowUtc = DateTime.UtcNow;
        var linkResult = PaymentLink.Create(
            command.TenantId,
            command.TaxpayerId,
            amountResult.Value,
            purposeResult.Value,
            PaymentLinkToken.Generate(),
            command.Expiration,
            command.ActorUserId,
            nowUtc
        );
        if (linkResult.IsFailure)
            return Result.Failure<CreatePaymentLinkResponse>(linkResult.Error);

        var link = linkResult.Value;
        await links.AddAsync(link, ct);

        metrics.RecordPaymentLinkCreated();

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(PaymentLink),
            link.Id,
            PaymentAuditAction.PaymentLinkCreated,
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                link.Amount.AmountCents,
                link.Amount.Currency,
                link.ExpiresAtUtc,
            },
            reason: null,
            nowUtc,
            ct
        );

        await bus.PublishAsync(
            new PaymentLinkCreatedIntegrationEvent
            {
                TenantId = command.TenantId,
                PaymentLinkId = link.Id,
                TaxpayerId = link.TaxpayerId,
                AmountCents = link.Amount.AmountCents,
                Currency = link.Amount.Currency,
                Token = link.Token.Value,
                ExpiresAtUtc = link.ExpiresAtUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new CreatePaymentLinkResponse(link.Id, link.Token.Value, link.ExpiresAtUtc));
    }
}
