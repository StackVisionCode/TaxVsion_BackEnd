using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.ProviderCustomers.Queries;

public sealed record GetTenantProviderCustomerQuery(Guid TenantId, PaymentProviderCode Provider);
