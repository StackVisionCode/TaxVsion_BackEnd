namespace TaxVision.Notification.Application.Abstractions;

public interface ICustomerAssignmentAudienceRepository
{
    Task<IReadOnlyCollection<Guid>> GetAssignedUserIdsAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    );
}
