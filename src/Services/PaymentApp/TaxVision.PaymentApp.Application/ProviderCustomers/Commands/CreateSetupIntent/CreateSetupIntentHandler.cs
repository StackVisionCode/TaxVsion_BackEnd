using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.CreateSetupIntent;

public static class CreateSetupIntentHandler
{
    public static async Task<Result<SetupIntentResponse>> Handle(
        CreateSetupIntentCommand command,
        ITenantProviderCustomerRepository customers,
        IPaymentAdapterFactory providerFactory,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var adapter = providerFactory.Resolve(command.Provider);

        // El SetupIntent necesita el customer del provider. Se aprovisiona (y persiste) igual que en
        // AttachPaymentMethod, para que la tarjeta que el front confirme quede en ESTE mismo customer.
        var customer = await customers.GetByTenantAndProviderAsync(command.TenantId, command.Provider, ct);
        if (customer is null)
        {
            var provisionResult = await ProvisionCustomerAsync(command.TenantId, adapter, nowUtc, ct);
            if (provisionResult.IsFailure)
                return Result.Failure<SetupIntentResponse>(provisionResult.Error);

            customer = provisionResult.Value;
            await customers.AddAsync(customer, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        var token = new ProviderCustomerToken(customer.CustomerReference.Value, customer.ProviderCode);
        var result = await adapter.CreateSetupIntentAsync(token, ct);
        if (result.IsFailure)
            return Result.Failure<SetupIntentResponse>(result.Error);

        return Result.Success(new SetupIntentResponse(result.Value.ClientSecret));
    }

    private static async Task<Result<TenantProviderCustomer>> ProvisionCustomerAsync(
        Guid tenantId,
        IPaymentProvider adapter,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var tokenResult = await adapter.GetOrCreateCustomerAsync(tenantId, SyntheticPayer.EmailFor(tenantId), null, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<TenantProviderCustomer>(tokenResult.Error);

        var referenceResult = ProviderCustomerReference.Create(adapter.Code, tokenResult.Value.Token);
        if (referenceResult.IsFailure)
            return Result.Failure<TenantProviderCustomer>(referenceResult.Error);

        return TenantProviderCustomer.Register(
            tenantId,
            adapter.Code,
            referenceResult.Value,
            SyntheticPayer.EmailFor(tenantId),
            nowUtc
        );
    }
}
