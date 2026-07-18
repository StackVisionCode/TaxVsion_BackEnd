using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.AttachPaymentMethod;

public static class AttachPaymentMethodHandler
{
    public static async Task<Result<Guid>> Handle(
        AttachPaymentMethodCommand command,
        ITenantProviderCustomerRepository customers,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantProviderCustomer> logger,
        CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var adapter = providerFactory.Resolve(command.Provider);

        var customer = await customers.GetByTenantAndProviderAsync(command.TenantId, command.Provider, ct);
        var isNewCustomer = customer is null;
        if (customer is null)
        {
            var provisionResult = await ProvisionCustomerAsync(command.TenantId, adapter, nowUtc, ct);
            if (provisionResult.IsFailure)
                return Result.Failure<Guid>(provisionResult.Error);

            customer = provisionResult.Value;
        }

        var customerToken = new ProviderCustomerToken(customer.CustomerReference.Value, customer.ProviderCode);
        var attachResult = await adapter.AttachPaymentMethodAsync(customerToken, command.PaymentMethodReference, ct);
        if (attachResult.IsFailure)
            return Result.Failure<Guid>(attachResult.Error);

        var info = attachResult.Value;
        var domainResult = customer.AttachPaymentMethod(
            info.MethodReference, info.Brand, info.Last4, info.ExpMonth, info.ExpYear, command.SetAsDefault, nowUtc);
        if (domainResult.IsFailure)
            return Result.Failure<Guid>(domainResult.Error);

        if (isNewCustomer)
            await customers.AddAsync(customer, ct);

        await AuditEntryFactory.AppendAsync(
            audit, customer.TenantId, nameof(TenantProviderCustomer), customer.Id, PaymentAuditAction.PaymentMethodAttached,
            command.ActorUserId, correlation.CorrelationId,
            before: (object?)null,
            after: new { info.Brand, info.Last4, command.SetAsDefault },
            reason: null, nowUtc, ct);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Payment method {Brand} ****{Last4} attached for tenant {TenantId} on {Provider}.",
            info.Brand, info.Last4, command.TenantId, command.Provider);

        return Result.Success(domainResult.Value);
    }

    /// <summary>Fallback lazy: si el tenant fue creado antes de que existiera el
    /// aprovisionamiento eager en <c>TenantCreatedConsumer</c> (§D.4), o si el email real
    /// del admin nunca llegó, se usa el email sintético — mismo mecanismo que
    /// <see cref="SyntheticPayer"/> usa para cobros automáticos.</summary>
    private static async Task<Result<TenantProviderCustomer>> ProvisionCustomerAsync(
        Guid tenantId, IPaymentProvider adapter, DateTime nowUtc, CancellationToken ct)
    {
        var tokenResult = await adapter.GetOrCreateCustomerAsync(tenantId, SyntheticPayer.EmailFor(tenantId), null, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<TenantProviderCustomer>(tokenResult.Error);

        var referenceResult = ProviderCustomerReference.Create(adapter.Code, tokenResult.Value.Token);
        if (referenceResult.IsFailure)
            return Result.Failure<TenantProviderCustomer>(referenceResult.Error);

        return TenantProviderCustomer.Register(tenantId, adapter.Code, referenceResult.Value, SyntheticPayer.EmailFor(tenantId), nowUtc);
    }
}
