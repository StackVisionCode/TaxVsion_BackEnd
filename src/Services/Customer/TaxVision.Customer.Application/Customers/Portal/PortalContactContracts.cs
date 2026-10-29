namespace TaxVision.Customer.Application.Customers.Portal;

// Autoservicio del cliente del portal sobre SU ficha: el CustomerId sale siempre del token.

public sealed record GetMyCustomerContactQuery(Guid TenantId, Guid CustomerId);

public sealed record MyCustomerContactResponse(string? PrimaryPhone, string PrimaryEmail);

public sealed record UpdateMyCustomerPhoneCommand(Guid TenantId, Guid CustomerId, Guid UserId, string? PhoneNumber);
