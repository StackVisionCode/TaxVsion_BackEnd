using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Profiles.EffectiveSignature;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Permissions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Preparer;

/// <summary>Resultado de un cambio de preparador: quién era y quién quedó (Changed=false si ya era él).</summary>
public sealed record PreparerReassignment(Guid? PreviousPreparerUserId, PreparerInfo NewPreparer, bool Changed);

/// <summary>
/// Cambia el preparador de una solicitud enviada a otro empleado — lo usan el retiro (sistema, con el
/// sucesor) y el admin a mano. Reglas: el nuevo debe estar activo en la oficina y tener su propia
/// identidad 8879 declarada (nunca se le inventa un PTIN); la firma reutilizable de la oficina se
/// conserva y la personal del anterior se sustituye por la efectiva del nuevo. El cambio se audita en
/// la cadena HMAC ANTES de mutar la solicitud: si la auditoría falla, la solicitud queda intacta.
/// No guarda: el que llama hace SaveChanges.
/// </summary>
public sealed class PreparerReassigner(
    ISignatureRequestStaffWorkRepository staffWork,
    IAuthzUserPermissionsProjectionRepository permissions,
    ISignatureProfileRepository profiles,
    IEffectiveSignatureResolver effectiveSignatures,
    IAuditChainAppender audit,
    PreparerPermissionCodes permissionCodes
)
{
    public static readonly Error UserNotActive = new(
        "Signature.Preparer.UserNotActive",
        "The selected user is not an active member of this office."
    );

    public static readonly Error NoIdentityOnFile = new(
        "Signature.Preparer.NoIdentityOnFile",
        "The selected user has no preparer identity (PTIN/EFIN) on file. They must enter it on one of their own requests first."
    );

    public static readonly Error AuditUnavailable = new(
        "Signature.Preparer.AuditUnavailable",
        "The change could not be recorded in the audit trail, so the preparer was not changed."
    );

    public async Task<Result<PreparerReassignment>> ReassignAsync(
        SignatureRequest request,
        Guid newPreparerUserId,
        Guid? changedByUserId,
        string reason,
        CancellationToken ct
    )
    {
        var previous = request.Preparer;
        var allowed = request.CanChangePreparer(newPreparerUserId);
        if (allowed.IsFailure)
            return Result.Failure<PreparerReassignment>(allowed.Error);

        if (previous!.UserId == newPreparerUserId)
            return Result.Success(new PreparerReassignment(previous.UserId, previous, Changed: false));

        // Fail-closed: sin proyección local (o inactiva) no hay prueba de que siga en la oficina.
        var member = await permissions.GetAsync(request.TenantId, newPreparerUserId, ct);
        if (member is not { IsActive: true })
            return Result.Failure<PreparerReassignment>(UserNotActive);

        var identity = await staffWork.FindLatestPreparerIdentityAsync(request.TenantId, newPreparerUserId, ct);
        if (identity is null)
            return Result.Failure<PreparerReassignment>(NoIdentityOnFile);

        var newPreparer = PreparerInfo.Create(
            identity.PtinOrEfin,
            identity.DisplayName,
            identity.TitleLabel,
            newPreparerUserId
        );
        if (newPreparer.IsFailure)
            return Result.Failure<PreparerReassignment>(newPreparer.Error);

        var signatureFileId = await ResolveSignatureFileAsync(request, newPreparerUserId, member, ct);

        var appended = await audit.AppendAsync(
            request.TenantId,
            request.Id,
            SignatureAuditEventKind.PreparerChanged,
            DateTime.UtcNow,
            new
            {
                PreviousPreparerUserId = previous.UserId,
                PreviousPtinOrEfin = previous.PtinOrEfin,
                PreviousDisplayName = previous.DisplayName,
                NewPreparerUserId = newPreparerUserId,
                NewPtinOrEfin = newPreparer.Value.PtinOrEfin,
                NewDisplayName = newPreparer.Value.DisplayName,
                SignatureReplaced = signatureFileId != request.PreparerSignatureFileId,
                ChangedByUserId = changedByUserId,
                Reason = reason,
            },
            ct
        );
        if (appended.IsFailure)
            return Result.Failure<PreparerReassignment>(AuditUnavailable);

        var changed = request.ChangePreparer(newPreparer.Value, signatureFileId);
        if (changed.IsFailure)
            return Result.Failure<PreparerReassignment>(changed.Error);

        return Result.Success(new PreparerReassignment(previous.UserId, newPreparer.Value, Changed: true));
    }

    // La firma de oficina no es de nadie: se conserva. Una personal (del anterior o de otro) se cambia por
    // la efectiva del nuevo; sin ninguna, el sellado cae al sello tipográfico con el nombre del nuevo.
    private async Task<Guid?> ResolveSignatureFileAsync(
        SignatureRequest request,
        Guid newPreparerUserId,
        AuthzUserPermissionsProjection member,
        CancellationToken ct
    )
    {
        if (request.PreparerSignatureFileId is not { } current)
            return null;

        var profile = await profiles.GetByFileIdAsync(request.TenantId, current, ct);
        if (profile is { IsOffice: true })
            return current;

        var canSignOwn = member.PermissionCodes().Contains(permissionCodes.SignOwn);
        var effective = await effectiveSignatures.ResolveAsync(request.TenantId, newPreparerUserId, canSignOwn, ct);
        return effective.IsSuccess ? effective.Value.FileId : null;
    }
}
