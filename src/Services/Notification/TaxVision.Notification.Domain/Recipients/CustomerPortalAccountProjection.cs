using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Recipients;

/// <summary>Local projection from customer to portal user.</summary>
public sealed class CustomerPortalAccountProjection : TenantEntity
{
    private CustomerPortalAccountProjection() { }

    public Guid CustomerId { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static CustomerPortalAccountProjection Create(
        Guid tenantId,
        Guid customerId,
        Guid userId,
        bool isActive = true
    )
    {
        var projection = new CustomerPortalAccountProjection
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            UserId = userId,
            IsActive = isActive,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void Synchronize(Guid userId, bool isActive)
    {
        UserId = userId;
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkActive(Guid userId)
    {
        UserId = userId;
        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkInactive()
    {
        if (!IsActive)
            return;
        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
