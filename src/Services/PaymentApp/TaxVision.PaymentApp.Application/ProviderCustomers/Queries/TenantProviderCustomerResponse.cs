namespace TaxVision.PaymentApp.Application.ProviderCustomers.Queries;

public sealed record TenantProviderCustomerResponse(Guid Id, string ProviderCode, string Email, IReadOnlyList<SavedPaymentMethodResponse> SavedMethods);

public sealed record SavedPaymentMethodResponse(Guid Id, string Brand, string Last4, int ExpMonth, int ExpYear, bool IsDefault);
