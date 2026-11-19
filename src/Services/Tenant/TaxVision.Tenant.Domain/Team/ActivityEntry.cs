using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.Team;

/// <summary>
/// Una acción de un miembro del equipo, tomada de un evento con actor explícito. Registro de lectura, solo se
/// agrega: no cambia de estado ni emite eventos. Sin títulos ni contenido; el CRM resuelve con sus permisos.
/// </summary>
public sealed class ActivityEntry : TenantEntity
{
    private ActivityEntry() { }

    /// <summary>Evento de origen: con el tenant, impide registrar dos veces la misma acción.</summary>
    public Guid EventId { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "Autoría histórica; el retiro no la cambia.")]
    public Guid ActorUserId { get; private set; }

    public string Verb { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public Guid ResourceId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public ActivityVisibility Visibility { get; private set; }

    /// <summary>Cuándo ocurrió según el evento, no cuándo llegó.</summary>
    public DateTime OccurredAtUtc { get; private set; }

    public static Result<ActivityEntry> Create(ActivityEntryDraft draft)
    {
        var invalid = Validate(draft);
        if (invalid is not null)
            return Result.Failure<ActivityEntry>(invalid);

        var entry = new ActivityEntry
        {
            Id = Guid.NewGuid(),
            EventId = draft.EventId,
            ActorUserId = draft.ActorUserId,
            Verb = draft.Verb,
            ResourceType = ActivityVerbs.ResourceTypeOf(draft.Verb)!,
            ResourceId = draft.ResourceId,
            CustomerId = draft.CustomerId == Guid.Empty ? null : draft.CustomerId,
            Visibility = draft.Visibility,
            OccurredAtUtc = draft.OccurredAtUtc,
        };
        entry.SetTenant(draft.TenantId);
        return Result.Success(entry);
    }

    private static Error? Validate(ActivityEntryDraft draft)
    {
        if (draft.TenantId == Guid.Empty)
            return ActivityEntryErrors.TenantRequired;
        if (draft.EventId == Guid.Empty)
            return ActivityEntryErrors.EventRequired;
        if (draft.ActorUserId == Guid.Empty)
            return ActivityEntryErrors.ActorRequired;
        if (!ActivityVerbs.IsKnown(draft.Verb))
            return ActivityEntryErrors.UnknownVerb;
        if (draft.ResourceId == Guid.Empty)
            return ActivityEntryErrors.ResourceRequired;
        if (!Enum.IsDefined(draft.Visibility))
            return ActivityEntryErrors.VisibilityInvalid;
        return draft.OccurredAtUtc == default ? ActivityEntryErrors.OccurredAtRequired : null;
    }
}
