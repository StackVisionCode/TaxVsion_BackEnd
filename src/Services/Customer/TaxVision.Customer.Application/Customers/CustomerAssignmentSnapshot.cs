using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers;

// Arma el evento snapshot de asignaciones desde el agregado (tras mutarlo y guardarlo). Un solo lugar para
// que todos los puntos de mutación publiquen el MISMO shape. Version = UpdatedAtUtc (monotónica por-cliente).
public static class CustomerAssignmentSnapshot
{
    public static CustomerAssignmentsChangedIntegrationEvent From(CustomerEntity customer, string correlationId) =>
        new()
        {
            TenantId = customer.TenantId,
            CorrelationId = correlationId,
            CustomerId = customer.Id,
            AssigneeUserIds = customer.Assignments.Select(a => a.UserId).ToList(),
            PrimaryUserId = customer.AssignedPreparerUserId,
            Version = customer.UpdatedAtUtc ?? DateTime.UtcNow,
        };
}
