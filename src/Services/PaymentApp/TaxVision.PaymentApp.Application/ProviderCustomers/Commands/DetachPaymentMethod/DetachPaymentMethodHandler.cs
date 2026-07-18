using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.ProviderCustomers;

namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.DetachPaymentMethod;

public static class DetachPaymentMethodHandler
{
    public static async Task<Result> Handle(
        DetachPaymentMethodCommand command,
        ITenantProviderCustomerRepository customers,
        IPaymentAdapterFactory providerFactory,
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

        var method = FindMethod(customer, command.PaymentMethodId);
        if (method is null)
            return Result.Failure(
                new Error(
                    "TenantProviderCustomer.MethodNotFound",
                    "Payment method does not exist or was already detached."
                )
            );

        var nowUtc = DateTime.UtcNow;
        var domainResult = customer.DetachPaymentMethod(command.PaymentMethodId, nowUtc);
        if (domainResult.IsFailure)
            return domainResult;

        var adapter = providerFactory.Resolve(customer.ProviderCode);
        var providerResult = await adapter.DetachPaymentMethodAsync(method.MethodReference, ct);
        if (providerResult.IsFailure)
        {
            logger.LogWarning(
                "Local detach succeeded but provider detach failed for method {MethodId}: {Error}. Provider state may drift.",
                command.PaymentMethodId,
                providerResult.Error.Message
            );
        }

        await AuditEntryFactory.AppendAsync(
            audit,
            customer.TenantId,
            nameof(TenantProviderCustomer),
            customer.Id,
            PaymentAuditAction.PaymentMethodDetached,
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new { method.Brand, method.Last4 },
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Payment method {MethodId} detached for tenant {TenantId}.",
            command.PaymentMethodId,
            command.TenantId
        );

        return Result.Success();
    }

    private static TenantSavedPaymentMethod? FindMethod(TenantProviderCustomer customer, Guid methodId)
    {
        foreach (var method in customer.SavedMethods)
        {
            if (method.Id == methodId)
                return method;
        }

        return null;
    }
}
