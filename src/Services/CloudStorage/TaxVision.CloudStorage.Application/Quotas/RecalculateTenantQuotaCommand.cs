using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Quotas;

/// <summary>
/// CST-5 (reposición): fija Used/Reserved del tenant con la regla de <see cref="FileObject.QuotaFootprint"/>.
/// Toma la fila con candado, así que no compite con Reserve/Commit; correrlo dos veces da lo mismo.
/// </summary>
public sealed record RecalculateTenantQuotaCommand(Guid TenantId, Guid ActorId);

public sealed record RecalculateTenantQuotaResponse(
    long UsedBytesBefore,
    long ReservedBytesBefore,
    long UsedBytesAfter,
    long ReservedBytesAfter
);

public static class RecalculateTenantQuotaHandler
{
    public static async Task<Result<RecalculateTenantQuotaResponse>> Handle(
        RecalculateTenantQuotaCommand command,
        IStorageLimitRepository limits,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var limit = await limits.GetForUpdateAsync(command.TenantId, ct);
        if (limit is null)
            return Result.Failure<RecalculateTenantQuotaResponse>(QuotaErrors.NotProvisioned);

        long used = 0;
        long reserved = 0;
        foreach (var input in await files.ListQuotaInputsAsync(command.TenantId, ct))
        {
            if (input.Footprint == QuotaFootprint.Used)
                used += input.SizeBytes;
            else if (input.Footprint == QuotaFootprint.Reserved)
                reserved += input.SizeBytes;
        }

        var response = new RecalculateTenantQuotaResponse(limit.UsedBytes, limit.ReservedBytes, used, reserved);
        var recalculated = limit.Recalculate(used, reserved);
        if (recalculated.IsFailure)
            return Result.Failure<RecalculateTenantQuotaResponse>(recalculated.Error);

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                null,
                command.ActorId,
                "quota.recalculated",
                "success",
                null,
                null,
                $"quota-recalc-{command.TenantId:N}",
                $"used={response.UsedBytesBefore}->{used};reserved={response.ReservedBytesBefore}->{reserved}",
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(response);
    }
}
