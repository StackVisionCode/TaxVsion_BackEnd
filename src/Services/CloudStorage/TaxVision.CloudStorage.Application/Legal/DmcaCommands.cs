using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Legal;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Legal;

/// <summary>
/// Fase L1.3 — registra un takedown DMCA: crea el expediente, bloquea el archivo
/// (BlockForTakedown) y lo pone bajo legal hold para que no pueda purgarse
/// mientras el reclamo sigue abierto. Platform-only (cloudstorage.legal.manage),
/// mismo criterio que SetLegalHoldCommand: no valida StorageActorScope.
/// </summary>
public sealed record RegisterDmcaTakedownCommand(
    Guid TenantId,
    Guid ActorId,
    Guid FileId,
    string ClaimantName,
    string ClaimantEmail,
    string CopyrightedWorkDescription,
    string InfringingMaterialDescription,
    bool SwornStatementAccepted,
    RequestAuditContext Audit
);

public static class RegisterDmcaTakedownHandler
{
    public static async Task<Result<Guid>> Handle(
        RegisterDmcaTakedownCommand command,
        IFileObjectRepository files,
        IDmcaNoticeRepository notices,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
            return Result.Failure<Guid>(FileErrors.NotFound);

        if (await notices.HasActiveNoticeForFileAsync(command.TenantId, command.FileId, ct))
            return Result.Failure<Guid>(DmcaErrors.ActiveNoticeAlreadyExists);

        var claimantEmail = ClaimantEmail.Create(command.ClaimantEmail);
        if (claimantEmail.IsFailure)
            return Result.Failure<Guid>(claimantEmail.Error);

        var now = clock.UtcNow;
        var registered = DmcaNotice.Register(
            Guid.NewGuid(),
            command.TenantId,
            command.FileId,
            command.ClaimantName,
            claimantEmail.Value,
            command.CopyrightedWorkDescription,
            command.InfringingMaterialDescription,
            command.SwornStatementAccepted,
            command.ActorId,
            now
        );
        if (registered.IsFailure)
            return Result.Failure<Guid>(registered.Error);

        var blocked = file.BlockForTakedown(now);
        if (blocked.IsFailure)
            return Result.Failure<Guid>(blocked.Error);

        if (!file.IsLegalHeld)
            file.PlaceLegalHold();

        var notice = registered.Value;
        notices.Add(notice);
        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                command.ActorId,
                "dmca.takedown.registered",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                $"noticeId={notice.Id};claimant={command.ClaimantName}",
                now
            )
        );
        await bus.PublishAsync(
            new FileBlockedByDmcaTakedownIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = file.Id,
                DmcaNoticeId = notice.Id,
                CreatedBy = file.CreatedBy,
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(notice.Id);
    }
}

/// <summary>Fase L1.3 — el uploader/tenant disputa un takedown recibido. Tenant-side (cloudstorage.file.dmca_counternotice), a diferencia del registro/reinstalacion.</summary>
public sealed record SubmitDmcaCounterNoticeCommand(
    Guid TenantId,
    Guid ActorId,
    Guid DmcaNoticeId,
    string CounterNoticeText,
    StorageActorScope Scope,
    RequestAuditContext Audit
);

public static class SubmitDmcaCounterNoticeHandler
{
    public static async Task<Result> Handle(
        SubmitDmcaCounterNoticeCommand command,
        IDmcaNoticeRepository notices,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var notice = await notices.GetAsync(command.TenantId, command.DmcaNoticeId, ct);
        if (notice is null)
            return Result.Failure(DmcaErrors.NotFound);

        var file = await files.GetAsync(command.TenantId, notice.FileId, ct);
        if (file is null || !command.Scope.CanAccess(file))
            return Result.Failure(FileErrors.NotFound);

        var now = clock.UtcNow;
        var submitted = notice.SubmitCounterNotice(command.CounterNoticeText, command.ActorId, now);
        if (submitted.IsFailure)
            return submitted;

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                notice.FileId,
                command.ActorId,
                "dmca.counter_notice.submitted",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                $"noticeId={notice.Id}",
                now
            )
        );
        await bus.PublishAsync(
            new DmcaCounterNoticeSubmittedIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = file.Id,
                DmcaNoticeId = notice.Id,
                CreatedBy = file.CreatedBy,
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>Fase L1.3 — el equipo legal cierra el expediente reinstalando el archivo. Platform-only (cloudstorage.legal.manage).</summary>
public sealed record ReinstateDmcaFileCommand(
    Guid TenantId,
    Guid ActorId,
    Guid DmcaNoticeId,
    string? ResolutionNotes,
    RequestAuditContext Audit
);

public static class ReinstateDmcaFileHandler
{
    public static async Task<Result> Handle(
        ReinstateDmcaFileCommand command,
        IDmcaNoticeRepository notices,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var notice = await notices.GetAsync(command.TenantId, command.DmcaNoticeId, ct);
        if (notice is null)
            return Result.Failure(DmcaErrors.NotFound);

        var now = clock.UtcNow;
        var reinstatedNotice = notice.Reinstate(command.ActorId, command.ResolutionNotes, now);
        if (reinstatedNotice.IsFailure)
            return reinstatedNotice;

        var file = await files.GetAsync(command.TenantId, notice.FileId, ct);
        if (file is null)
            return Result.Failure(FileErrors.NotFound);

        var reinstatedFile = file.ReinstateFromTakedown(now);
        if (reinstatedFile.IsFailure)
            return reinstatedFile;

        if (file.IsLegalHeld)
            file.LiftLegalHold();

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                command.ActorId,
                "dmca.file.reinstated",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                $"noticeId={notice.Id}",
                now
            )
        );
        await bus.PublishAsync(
            new FileReinstatedFromTakedownIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = file.Id,
                DmcaNoticeId = notice.Id,
                CreatedBy = file.CreatedBy,
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
