using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.ProviderCustomers;

namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.SetDefaultPaymentMethod;

public static class SetDefaultPaymentMethodHandler
{
    public static async Task<Result> Handle(
        SetDefaultPaymentMethodCommand command,
        ITenantProviderCustomerRepository customers,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantProviderCustomer> logger,
        CancellationToken ct
    )
    {
        var customer = await customers.GetByIdAsync(command.TenantProviderCustomerId, command.TenantId, ct);
        if (customer is null)
            return Result.Failure(
                new Error("TenantProviderCustomer.NotFound", "TenantProviderCustomer does not exist.")
            );

        var nowUtc = DateTime.UtcNow;
        var domainResult = customer.MarkPaymentMethodAsDefault(command.PaymentMethodId, nowUtc);
        if (domainResult.IsFailure)
            return domainResult;

        await AuditEntryFactory.AppendAsync(
            audit,
            customer.TenantId,
            nameof(TenantProviderCustomer),
            customer.Id,
            PaymentAuditAction.PaymentMethodSetDefault,
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new { command.PaymentMethodId },
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Payment method {MethodId} set as default for tenant {TenantId}.",
            command.PaymentMethodId,
            command.TenantId
        );

        return Result.Success();
    }
}
