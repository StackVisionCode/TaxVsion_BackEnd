using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>Lo que un evento dice que hizo un miembro. <see cref="ActorUserId"/> null o vacío = no se registra.</summary>
public sealed record ActivityFact(
    Guid? ActorUserId,
    string Verb,
    Guid ResourceId,
    Guid? CustomerId,
    ActivityVisibility Visibility = ActivityVisibility.Team
);
