using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team;

/// <summary>Una entrada del historial para el CRM: solo ids; nombres y títulos los resuelve el CRM con sus permisos.</summary>
public sealed record ActivityEntryView(
    Guid Id,
    Guid ActorUserId,
    string Verb,
    string ResourceType,
    Guid ResourceId,
    Guid? CustomerId,
    string Visibility,
    DateTime OccurredAtUtc,
    string? Link
)
{
    public static ActivityEntryView From(ActivityEntry entry) =>
        new(
            entry.Id,
            entry.ActorUserId,
            entry.Verb,
            entry.ResourceType,
            entry.ResourceId,
            entry.CustomerId,
            entry.Visibility.ToString(),
            entry.OccurredAtUtc,
            ActivityLinks.For(entry.ResourceType, entry.ResourceId, entry.CustomerId)
        );
}
