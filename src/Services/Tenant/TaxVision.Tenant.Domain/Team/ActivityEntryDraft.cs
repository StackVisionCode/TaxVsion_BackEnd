namespace TaxVision.Tenant.Domain.Team;

/// <summary>Datos de una acción tal como los trae el evento; <see cref="ActivityEntry.Create"/> los valida.</summary>
public sealed record ActivityEntryDraft(
    Guid TenantId,
    Guid EventId,
    Guid ActorUserId,
    string Verb,
    Guid ResourceId,
    Guid? CustomerId,
    ActivityVisibility Visibility,
    DateTime OccurredAtUtc
);
