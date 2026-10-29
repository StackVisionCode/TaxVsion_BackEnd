using BuildingBlocks.Results;
using TaxVision.Customer.Domain.Customers;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers.Avatars;

/// <summary>
/// Misma regla que el resto de escrituras: otro tenant o un cliente no asignado se comporta como
/// inexistente (404, no 403). El portal no ve su ficha archivada (igual que el teléfono); el staff
/// recibe el motivo.
/// </summary>
internal static class CustomerAvatarAccess
{
    public static readonly Error NotFound = new("Customer.NotFound", "Customer not found.");

    public static Result Check(
        CustomerEntity? customer,
        Guid tenantId,
        Guid actorUserId,
        CustomerAvatarCaller caller,
        bool callerCanViewAllCustomers
    )
    {
        if (customer is null || customer.TenantId != tenantId)
            return Result.Failure(NotFound);

        if (caller == CustomerAvatarCaller.PortalSelf)
            return customer.Status == CustomerStatus.Archived ? Result.Failure(NotFound) : Result.Success();

        if (!CustomerAccessPolicy.CanAccess(customer, actorUserId, callerCanViewAllCustomers))
            return Result.Failure(NotFound);

        return customer.Status == CustomerStatus.Archived
            ? Result.Failure(CustomerProfilePictureErrors.Archived)
            : Result.Success();
    }
}
