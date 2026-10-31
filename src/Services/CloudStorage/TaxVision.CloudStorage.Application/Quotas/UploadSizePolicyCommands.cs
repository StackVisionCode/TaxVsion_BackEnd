using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Quotas;

public sealed record UploadSizePolicyView(
    Guid Id,
    string Scope,
    string FolderType,
    string? PlanCode,
    Guid? TenantId,
    long MaxFileSizeBytes,
    Guid UpdatedByUserId,
    DateTime UpdatedAtUtc
);

internal static class UploadSizePolicyMapper
{
    public static UploadSizePolicyView Map(UploadSizePolicy policy) =>
        new(
            policy.Id,
            policy.Scope.ToString(),
            policy.FolderType.ToString(),
            policy.PlanCode,
            policy.TenantId,
            policy.MaxFileSizeBytes,
            policy.UpdatedByUserId,
            policy.UpdatedAtUtc
        );
}

/// <summary>Upsert de una fila de tope. Plan exige PlanCode y Tenant exige TenantId; Global ignora ambos.</summary>
public sealed record SetUploadSizePolicyCommand(
    UploadSizePolicyScope Scope,
    FolderType FolderType,
    string? PlanCode,
    Guid? TenantId,
    long MaxFileSizeBytes,
    Guid ActorUserId
);

public sealed record SetUploadSizePolicyResponse(UploadSizePolicyView Policy, bool Created);

public static class SetUploadSizePolicyHandler
{
    public static async Task<Result<SetUploadSizePolicyResponse>> Handle(
        SetUploadSizePolicyCommand command,
        IUploadSizePolicyRepository policies,
        IUploadSizePolicyAuditRepository audit,
        UploadPolicyCache cache,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var key = UploadSizePolicyKey.From(command.Scope, command.PlanCode, command.TenantId);
        if (key.IsFailure)
            return Result.Failure<SetUploadSizePolicyResponse>(key.Error);
        if (!Enum.IsDefined(command.FolderType))
            return Result.Failure<SetUploadSizePolicyResponse>(UploadSizePolicyErrors.InvalidFolderType);

        var now = clock.UtcNow;
        var (planCode, tenantId) = key.Value;
        var existing = await policies.GetAsync(command.Scope, command.FolderType, planCode, tenantId, ct);

        UploadSizePolicy policy;
        long? oldBytes;
        if (existing is null)
        {
            var created = command.Scope switch
            {
                UploadSizePolicyScope.Plan => UploadSizePolicy.CreatePlan(
                    planCode!,
                    command.FolderType,
                    command.MaxFileSizeBytes,
                    command.ActorUserId,
                    now
                ),
                UploadSizePolicyScope.Tenant => UploadSizePolicy.CreateTenant(
                    tenantId!.Value,
                    command.FolderType,
                    command.MaxFileSizeBytes,
                    command.ActorUserId,
                    now
                ),
                _ => UploadSizePolicy.CreateGlobal(
                    command.FolderType,
                    command.MaxFileSizeBytes,
                    command.ActorUserId,
                    now
                ),
            };
            if (created.IsFailure)
                return Result.Failure<SetUploadSizePolicyResponse>(created.Error);
            policy = created.Value;
            policies.Add(policy);
            oldBytes = null;
        }
        else
        {
            oldBytes = existing.MaxFileSizeBytes;
            var updated = existing.SetMaxFileSizeBytes(command.MaxFileSizeBytes, command.ActorUserId, now);
            if (updated.IsFailure)
                return Result.Failure<SetUploadSizePolicyResponse>(updated.Error);
            policy = existing;
        }

        audit.Add(
            UploadSizePolicyAudit.Record(
                policy.Scope,
                policy.FolderType,
                policy.PlanCode,
                policy.TenantId,
                oldBytes,
                policy.MaxFileSizeBytes,
                command.ActorUserId,
                now
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        // Tras guardar, no antes: una lectura concurrente no debe repoblar la caché con el valor viejo.
        cache.Invalidate();
        return Result.Success(new SetUploadSizePolicyResponse(UploadSizePolicyMapper.Map(policy), oldBytes is null));
    }
}

public sealed record RemoveUploadSizePolicyCommand(
    UploadSizePolicyScope Scope,
    FolderType FolderType,
    string? PlanCode,
    Guid? TenantId,
    Guid ActorUserId
);

public sealed record RemoveUploadSizePolicyResponse(bool Removed);

public static class RemoveUploadSizePolicyHandler
{
    public static async Task<Result<RemoveUploadSizePolicyResponse>> Handle(
        RemoveUploadSizePolicyCommand command,
        IUploadSizePolicyRepository policies,
        IUploadSizePolicyAuditRepository audit,
        UploadPolicyCache cache,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var key = UploadSizePolicyKey.From(command.Scope, command.PlanCode, command.TenantId);
        if (key.IsFailure)
            return Result.Failure<RemoveUploadSizePolicyResponse>(key.Error);

        var (planCode, tenantId) = key.Value;
        var existing = await policies.GetAsync(command.Scope, command.FolderType, planCode, tenantId, ct);
        // Idempotente: borrar lo que no existe no audita ni toca la BD.
        if (existing is null)
            return Result.Success(new RemoveUploadSizePolicyResponse(false));

        policies.Remove(existing);
        audit.Add(
            UploadSizePolicyAudit.Record(
                existing.Scope,
                existing.FolderType,
                existing.PlanCode,
                existing.TenantId,
                existing.MaxFileSizeBytes,
                null,
                command.ActorUserId,
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success(new RemoveUploadSizePolicyResponse(true));
    }
}

/// <summary>Normaliza la clave según el scope: lo que no aplica queda null para que el índice único no distinga variantes.</summary>
internal static class UploadSizePolicyKey
{
    public static Result<(string? PlanCode, Guid? TenantId)> From(
        UploadSizePolicyScope scope,
        string? planCode,
        Guid? tenantId
    ) =>
        scope switch
        {
            UploadSizePolicyScope.Global => Result.Success<(string?, Guid?)>((null, null)),
            UploadSizePolicyScope.Plan => string.IsNullOrWhiteSpace(planCode)
                ? Result.Failure<(string?, Guid?)>(UploadSizePolicyErrors.InvalidPlanCode)
                : Result.Success<(string?, Guid?)>((planCode.Trim(), null)),
            UploadSizePolicyScope.Tenant => tenantId is null || tenantId == Guid.Empty
                ? Result.Failure<(string?, Guid?)>(UploadSizePolicyErrors.InvalidTenant)
                : Result.Success<(string?, Guid?)>((null, tenantId)),
            _ => Result.Failure<(string?, Guid?)>(UploadSizePolicyErrors.InvalidFolderType),
        };
}
