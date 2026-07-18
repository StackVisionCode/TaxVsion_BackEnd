using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files.LegalHold;

/// <summary>
/// Fase L1.2 — platform-only (cloudstorage.legal.manage, ver StorageIdentity/Program.cs):
/// no valida StorageActorScope porque el equipo legal de la plataforma actua fuera del
/// aislamiento normal por customer, sobre cualquier archivo de cualquier tenant.
/// </summary>
public sealed record SetLegalHoldCommand(
    Guid TenantId,
    Guid ActorId,
    Guid FileId,
    string Reason,
    RequestAuditContext Audit
);

public static class SetLegalHoldHandler
{
    public static async Task<Result> Handle(
        SetLegalHoldCommand command,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
            return Result.Failure(FileErrors.NotFound);

        var placed = file.PlaceLegalHold();
        if (placed.IsFailure)
            return placed;

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                command.ActorId,
                "file.legal_hold_set",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                command.Reason,
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record LiftLegalHoldCommand(
    Guid TenantId,
    Guid ActorId,
    Guid FileId,
    string Reason,
    RequestAuditContext Audit
);

public static class LiftLegalHoldHandler
{
    public static async Task<Result> Handle(
        LiftLegalHoldCommand command,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
            return Result.Failure(FileErrors.NotFound);

        var lifted = file.LiftLegalHold();
        if (lifted.IsFailure)
            return lifted;

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                command.ActorId,
                "file.legal_hold_unset",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                command.Reason,
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
