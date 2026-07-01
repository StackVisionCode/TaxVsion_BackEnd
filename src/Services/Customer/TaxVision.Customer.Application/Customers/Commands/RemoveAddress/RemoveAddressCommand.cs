namespace TaxVision.Customer.Application.Customers.Commands.RemoveAddress;

public sealed record RemoveAddressCommand(Guid TenantId, Guid CustomerId, Guid AddressId, Guid ModifiedByUserId);
