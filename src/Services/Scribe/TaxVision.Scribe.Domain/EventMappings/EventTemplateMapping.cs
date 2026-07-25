using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Domain.EventMappings;

/// <summary>
/// Regla de resolución evento→template (ej. "auth.password_reset_requested.v1" → "auth.password_reset").
/// <see cref="EventKey"/>/<see cref="Scope"/>/<see cref="TenantId"/>/<see cref="Locale"/> son la
/// identidad de la fila (índice único en Infrastructure) y son inmutables tras crear — cambiarlos
/// significa borrar y recrear el mapping, no editarlo. Solo el contenido (a qué TemplateKey apunta,
/// prioridad, habilitado) es mutable vía <see cref="Rebind"/>.
/// </summary>
public sealed class EventTemplateMapping : BaseEntity, INullableTenantOwned
{
    private EventTemplateMapping() { }

    public Guid? TenantId { get; private set; }
    public TemplateScope Scope { get; private set; }
    public EventKey EventKey { get; private set; } = default!;
    public TemplateKey TemplateKey { get; private set; } = default!;
    public Locale? Locale { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static Result<EventTemplateMapping> CreateNew(
        TemplateScope scope,
        Guid? tenantId,
        EventKey eventKey,
        TemplateKey templateKey,
        Locale? locale,
        int priority,
        DateTime createdAtUtc
    )
    {
        if (scope == TemplateScope.Tenant && tenantId is null)
            return Result.Failure<EventTemplateMapping>(
                new Error("EventTemplateMapping.TenantRequired", "TenantId is required for Tenant-scoped mappings.")
            );

        if (scope == TemplateScope.System && tenantId is not null)
            return Result.Failure<EventTemplateMapping>(
                new Error("EventTemplateMapping.TenantNotAllowed", "TenantId must be null for System-scoped mappings.")
            );

        return Result.Success(
            new EventTemplateMapping
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Scope = scope,
                EventKey = eventKey,
                TemplateKey = templateKey,
                Locale = locale,
                Priority = priority,
                Enabled = true,
                CreatedAtUtc = createdAtUtc,
            }
        );
    }

    /// <summary>Reapunta el mapping a otro template y/o ajusta su prioridad/habilitación. No toca la identidad (EventKey/Scope/TenantId/Locale).</summary>
    public Result Rebind(TemplateKey templateKey, int priority, bool enabled, DateTime updatedAtUtc)
    {
        TemplateKey = templateKey;
        Priority = priority;
        Enabled = enabled;
        UpdatedAtUtc = updatedAtUtc;
        return Result.Success();
    }
}
