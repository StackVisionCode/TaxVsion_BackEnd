using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Application.ProviderCustomers.Queries;

public static class GetTenantProviderCustomerHandler
{
    public static async Task<Result<TenantProviderCustomerResponse>> Handle(
        GetTenantProviderCustomerQuery query, ITenantProviderCustomerRepository customers, CancellationToken ct)
    {
        var customer = await customers.GetByTenantAndProviderAsync(query.TenantId, query.Provider, ct);
        if (customer is null)
            return Result.Failure<TenantProviderCustomerResponse>(new Error("TenantProviderCustomer.NotFound", "TenantProviderCustomer does not exist."));

        var methods = new List<SavedPaymentMethodResponse>();
        foreach (var method in customer.SavedMethods)
        {
            if (!method.IsDetached)
                methods.Add(new SavedPaymentMethodResponse(method.Id, method.Brand, method.Last4, method.ExpMonth, method.ExpYear, method.IsDefault));
        }

        return Result.Success(new TenantProviderCustomerResponse(customer.Id, customer.ProviderCode.ToString(), customer.Email, methods));
    }
}
