using BuildingBlocks.Domain;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Domain.Events;

public sealed record TenantStatusChangedDomainEvent(
    Guid TenantId,
    EnumTenantStatus.TenantStatus Status,
    bool IsActive,
    long Version
) : IDomainEvent;
