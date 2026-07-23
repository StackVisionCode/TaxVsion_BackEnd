using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Domain.Audit;

/// <summary>
/// Rastro mínimo de auditoría — plan §26, referenciado desde la §23 pero recién materializado acá
/// (Fase 14) porque es la primera acción de Correspondence con riesgo real de negocio (un correo
/// sale por el proveedor conectado, no hay "deshacer"). Deliberadamente sin lado de lectura: esta
/// fase solo escribe una fila por intento de envío (éxito o falla) — un endpoint de consulta/listado
/// es YAGNI hasta que exista un caso de uso real que lo necesite (ver nota en el plan §36 Fase 14
/// sobre alcance mínimo). Append-only por diseño, igual que cualquier bitácora: no hay
/// transiciones de estado que modelar, por eso no hereda la forma de aggregate con Result-returning
/// mutators del resto de este bounded context (<see cref="Compose.Draft"/>).
/// </summary>
public sealed class CorrespondenceAuditLog : ITenantOwned
{
    public const int ActionMaxLength = 100;
    public const int TargetTypeMaxLength = 100;
    public const int CorrelationIdMaxLength = 100;
    public const int DetailMaxLength = 1000;

    private CorrespondenceAuditLog() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Action { get; private set; } = default!;
    public string TargetType { get; private set; } = default!;
    public Guid TargetId { get; private set; }

    /// <summary>Null solo para acciones disparadas por un proceso de sistema, no por un usuario — Fase 14 siempre la llena (el envío es siempre HTTP-triggered por un usuario autenticado).</summary>
    public Guid? UserId { get; private set; }
    public string CorrelationId { get; private set; } = default!;
    public string Detail { get; private set; } = default!;
    public DateTime TimestampUtc { get; private set; }

    /// <summary>RBAC Fase 5 (RBAC_Hardening_Plan.md) — ver <see cref="Compose.Draft.SetTenant"/>.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static Result<CorrespondenceAuditLog> Record(
        Guid tenantId,
        string action,
        string targetType,
        Guid targetId,
        Guid? userId,
        string correlationId,
        string detail
    )
    {
        var validationError = Validate(tenantId, action, targetType, targetId, correlationId, detail);
        if (validationError is not null)
            return Result.Failure<CorrespondenceAuditLog>(validationError);

        return Result.Success(
            new CorrespondenceAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                UserId = userId,
                CorrelationId = correlationId,
                Detail = Truncate(detail, DetailMaxLength),
                TimestampUtc = DateTime.UtcNow,
            }
        );
    }

    private static Error? Validate(
        Guid tenantId,
        string action,
        string targetType,
        Guid targetId,
        string correlationId,
        string detail
    )
    {
        if (tenantId == Guid.Empty)
            return new Error("CorrespondenceAuditLog.TenantIdRequired", "TenantId is required.");
        if (string.IsNullOrWhiteSpace(action) || action.Length > ActionMaxLength)
            return new Error(
                "CorrespondenceAuditLog.Action",
                $"Action is required and must be at most {ActionMaxLength} chars."
            );
        if (string.IsNullOrWhiteSpace(targetType) || targetType.Length > TargetTypeMaxLength)
            return new Error(
                "CorrespondenceAuditLog.TargetType",
                $"TargetType is required and must be at most {TargetTypeMaxLength} chars."
            );
        if (targetId == Guid.Empty)
            return new Error("CorrespondenceAuditLog.TargetIdRequired", "TargetId is required.");
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > CorrelationIdMaxLength)
            return new Error(
                "CorrespondenceAuditLog.CorrelationId",
                $"CorrelationId is required and must be at most {CorrelationIdMaxLength} chars."
            );
        if (string.IsNullOrWhiteSpace(detail))
            return new Error("CorrespondenceAuditLog.DetailRequired", "Detail is required.");

        return null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
