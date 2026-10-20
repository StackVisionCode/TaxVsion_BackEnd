using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Recipients;

/// <summary>Local TenantAdmin projection used to resolve the tenant owner.</summary>
public sealed class TenantOwnerProjection : TenantEntity
{
    private TenantOwnerProjection() { }

    public Guid UserId { get; private set; }
    public DateTime RegisteredAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantOwnerProjection Create(
        Guid tenantId,
        Guid userId,
        DateTime registeredAtUtc,
        bool isActive = true
    )
    {
        var projection = new TenantOwnerProjection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RegisteredAtUtc = registeredAtUtc,
            IsActive = isActive,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void Synchronize(DateTime registeredAtUtc, bool isActive)
    {
        RegisteredAtUtc = registeredAtUtc;
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkActive()
    {
        if (IsActive)
            return;
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
