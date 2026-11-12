using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Catalog;

public enum CatalogAuditActorKind
{
    PlatformAdmin,
    Deploy,
    System,
}

/// <summary>Quién cambió el catálogo: un admin de plataforma, una revisión al desplegar o el sistema.</summary>
public sealed record CatalogAuditActor(CatalogAuditActorKind Kind, Guid? UserId, string Label)
{
    /// <summary>El usuario del comando; <see cref="Guid.Empty"/> es el sistema (por ejemplo el reconciliador).</summary>
    public static CatalogAuditActor ForUser(Guid userId) =>
        userId == Guid.Empty
            ? System("system")
            : new CatalogAuditActor(CatalogAuditActorKind.PlatformAdmin, userId, $"platform-admin:{userId}");

    public static CatalogAuditActor Deploy(string revisionId) =>
        new(CatalogAuditActorKind.Deploy, null, $"deploy:{revisionId}");

    public static CatalogAuditActor System(string name) =>
        new(
            CatalogAuditActorKind.System,
            null,
            name.StartsWith("system", StringComparison.Ordinal) ? name : $"system:{name}"
        );
}

/// <summary>Sobre qué parte del catálogo: tipo, id y código legible.</summary>
public sealed record CatalogAuditTarget(string EntityType, Guid? EntityId, string? EntityCode);

/// <summary>
/// D13: una entrada de la auditoría de plataforma del catálogo. Inmutable: solo se agrega, nunca se edita ni se
/// borra. Lleva quién, qué, sobre qué, antes y después (JSON), motivo, correlación y fecha.
/// </summary>
public sealed class CatalogAuditEntry : BaseEntity
{
    public const int MaxReasonLength = 500;

    private CatalogAuditEntry() { }

    public CatalogAuditActorKind ActorKind { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? ActorUserId { get; private set; }

    public string ActorLabel { get; private set; } = default!;
    public string Action { get; private set; } = default!;
    public string EntityType { get; private set; } = default!;
    public Guid? EntityId { get; private set; }
    public string? EntityCode { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? Reason { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }

    public static Result<CatalogAuditEntry> Record(
        CatalogAuditActor actor,
        string action,
        CatalogAuditTarget target,
        string? beforeJson,
        string? afterJson,
        string? reason,
        string? correlationId,
        DateTime nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(action))
            return Result.Failure<CatalogAuditEntry>(
                new Error("CatalogAudit.ActionRequired", "An audit entry needs an action.")
            );

        if (string.IsNullOrWhiteSpace(target.EntityType))
            return Result.Failure<CatalogAuditEntry>(
                new Error("CatalogAudit.EntityRequired", "An audit entry needs the entity it is about.")
            );

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is { Length: > MaxReasonLength })
            trimmedReason = trimmedReason[..MaxReasonLength];

        return Result.Success(
            new CatalogAuditEntry
            {
                ActorKind = actor.Kind,
                ActorUserId = actor.UserId,
                ActorLabel = actor.Label,
                Action = action.Trim(),
                EntityType = target.EntityType.Trim(),
                EntityId = target.EntityId,
                EntityCode = target.EntityCode,
                BeforeJson = beforeJson,
                AfterJson = afterJson,
                Reason = trimmedReason,
                CorrelationId = correlationId ?? string.Empty,
                OccurredAtUtc = nowUtc,
            }
        );
    }
}
