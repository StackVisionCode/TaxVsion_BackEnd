using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Legal;

/// <summary>
/// Fase L1.3 — expediente de una notificacion DMCA (17 U.S.C. § 512) registrada por
/// el equipo legal de la plataforma contra un archivo puntual. El bloqueo del
/// archivo y el legal hold los ejecuta el handler de aplicacion sobre FileObject;
/// este agregado solo lleva el registro/estado del tramite DMCA en si.
/// </summary>
public sealed class DmcaNotice : TenantEntity
{
    private DmcaNotice() { }

    public Guid FileId { get; private set; }
    public string ClaimantName { get; private set; } = default!;
    public string ClaimantEmail { get; private set; } = default!;
    public string CopyrightedWorkDescription { get; private set; } = default!;
    public string InfringingMaterialDescription { get; private set; } = default!;
    public DmcaNoticeStatus Status { get; private set; }
    public Guid RegisteredByActorId { get; private set; }
    public DateTime RegisteredAtUtc { get; private set; }
    public string? CounterNoticeText { get; private set; }
    public Guid? CounterNoticeSubmittedByActorId { get; private set; }
    public DateTime? CounterNoticeSubmittedAtUtc { get; private set; }
    public Guid? ResolvedByActorId { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public string? ResolutionNotes { get; private set; }

    public static Result<DmcaNotice> Register(
        Guid id,
        Guid tenantId,
        Guid fileId,
        string claimantName,
        ClaimantEmail claimantEmail,
        string copyrightedWorkDescription,
        string infringingMaterialDescription,
        bool swornStatementAccepted,
        Guid registeredByActorId,
        DateTime nowUtc
    )
    {
        if (!swornStatementAccepted)
            return Result.Failure<DmcaNotice>(DmcaErrors.SwornStatementRequired);
        if (string.IsNullOrWhiteSpace(claimantName))
            return Result.Failure<DmcaNotice>(DmcaErrors.DescriptionRequired);
        if (
            string.IsNullOrWhiteSpace(copyrightedWorkDescription)
            || string.IsNullOrWhiteSpace(infringingMaterialDescription)
        )
            return Result.Failure<DmcaNotice>(DmcaErrors.DescriptionRequired);

        var notice = new DmcaNotice
        {
            Id = id,
            FileId = fileId,
            ClaimantName = claimantName,
            ClaimantEmail = claimantEmail.Value,
            CopyrightedWorkDescription = copyrightedWorkDescription,
            InfringingMaterialDescription = infringingMaterialDescription,
            Status = DmcaNoticeStatus.Received,
            RegisteredByActorId = registeredByActorId,
            RegisteredAtUtc = nowUtc,
        };
        notice.SetTenant(tenantId);
        return Result.Success(notice);
    }

    /// <summary>El uploader/tenant disputa el reclamo. No reinstala el archivo: el equipo legal resuelve manualmente tras el periodo de espera de ley.</summary>
    public Result SubmitCounterNotice(string counterNoticeText, Guid actorId, DateTime nowUtc)
    {
        if (Status != DmcaNoticeStatus.Received)
            return Result.Failure(DmcaErrors.InvalidTransition);
        if (string.IsNullOrWhiteSpace(counterNoticeText))
            return Result.Failure(DmcaErrors.CounterNoticeTextRequired);

        Status = DmcaNoticeStatus.CounterNoticeSubmitted;
        CounterNoticeText = counterNoticeText;
        CounterNoticeSubmittedByActorId = actorId;
        CounterNoticeSubmittedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>El equipo legal cierra el expediente reinstalando el archivo (reclamo retirado o contranotificacion aceptada).</summary>
    public Result Reinstate(Guid actorId, string? resolutionNotes, DateTime nowUtc)
    {
        if (Status is not (DmcaNoticeStatus.Received or DmcaNoticeStatus.CounterNoticeSubmitted))
            return Result.Failure(DmcaErrors.InvalidTransition);

        Status = DmcaNoticeStatus.Reinstated;
        ResolvedByActorId = actorId;
        ResolvedAtUtc = nowUtc;
        ResolutionNotes = resolutionNotes;
        return Result.Success();
    }
}
