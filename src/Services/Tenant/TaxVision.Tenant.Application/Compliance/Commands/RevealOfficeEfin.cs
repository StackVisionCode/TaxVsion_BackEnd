using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Domain.Compliance;

namespace TaxVision.Tenant.Application.Compliance.Commands;

/// <summary>Un administrador pide ver el EFIN completo de su oficina.</summary>
public sealed record RevealOfficeEfinCommand(Guid TenantId, Guid ActorId);

public sealed record RevealedEfinResponse(string Efin);

/// <summary>
/// Descifra el EFIN y deja constancia (quién, cuándo) ANTES de devolverlo: si la constancia no se puede
/// guardar, el EFIN no sale. Sin EFIN guardado → <see cref="OfficeComplianceErrors.EfinNotSet"/>.
/// </summary>
public static class RevealOfficeEfinHandler
{
    public static async Task<Result<RevealedEfinResponse>> Handle(
        RevealOfficeEfinCommand command,
        IOfficeComplianceRepository compliances,
        IEfinProtector protector,
        IUnitOfWork unitOfWork,
        ILogger<RevealOfficeEfinCommand> logger,
        CancellationToken ct
    )
    {
        var compliance = await compliances.GetAsync(command.TenantId, ct);
        if (compliance?.Efin is null)
            return Result.Failure<RevealedEfinResponse>(OfficeComplianceErrors.EfinNotSet);

        var record = compliance.RecordEfinReveal(command.ActorId, DateTime.UtcNow);
        if (record.IsFailure)
            return Result.Failure<RevealedEfinResponse>(record.Error);

        var efin = protector.Reveal(compliance.Efin);
        await compliances.AddRevealRecordAsync(record.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogWarning("EFIN of tenant {TenantId} revealed to user {UserId}.", command.TenantId, command.ActorId);
        return Result.Success(new RevealedEfinResponse(efin));
    }
}
