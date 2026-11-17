using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Codes.IssueTenantReferralCode;

namespace TaxVision.Referrals.Application.Codes.AutoIssueTenantReferralCode;

/// <summary>
/// La oficina nueva recibe su código de referido al completarse el alta. <paramref name="RequestedAtUtc"/> es la fecha
/// del evento: fija el vencimiento, así un replay es el mismo pedido. La clave es determinista por oficina.
/// </summary>
public sealed record AutoIssueTenantReferralCodeCommand(Guid TenantId, Guid OwnerUserId, DateTime RequestedAtUtc)
{
    public string? IdempotencyKey { get; init; }

    public string EffectiveIdempotencyKey => IdempotencyKey ?? $"referral-auto:{TenantId:N}";
}

public enum AutoIssueTenantReferralCodeOutcome
{
    Issued,

    /// <summary>La oficina ya tenía un código activo (por ejemplo, el owner lo generó): se queda el suyo.</summary>
    AlreadyHasCode,

    /// <summary>No hay programa activo (o está suspendido o terminado): no se emite nada.</summary>
    NoActiveProgram,
}

public static class AutoIssueTenantReferralCodeHandler
{
    private const int DefaultValidityYears = 5;

    public static async Task<Result<AutoIssueTenantReferralCodeOutcome>> Handle(
        AutoIssueTenantReferralCodeCommand command,
        IReferralProgramRepository programs,
        IReferralCodeRepository codes,
        IReferralCodeTokenGenerator tokenGenerator,
        IReferralCodeTokenHasher tokenHasher,
        IReferralIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var program = await programs.GetActivePlatformProgramAsync(ct);
        if (program is null)
            return Result.Success(AutoIssueTenantReferralCodeOutcome.NoActiveProgram);

        var requestedAtUtc = DateTime.SpecifyKind(command.RequestedAtUtc, DateTimeKind.Utc);
        var expiresAtUtc = requestedAtUtc.AddYears(DefaultValidityYears);
        if (program.EndsAtUtc is { } programEnd && expiresAtUtc > programEnd)
            expiresAtUtc = programEnd;

        var issued = await IssueTenantReferralCodeHandler.Handle(
            new IssueTenantReferralCodeCommand(
                command.TenantId,
                program.Id,
                expiresAtUtc,
                command.OwnerUserId,
                command.EffectiveIdempotencyKey,
                command.OwnerUserId
            ),
            programs,
            codes,
            tokenGenerator,
            tokenHasher,
            idempotency,
            timeProvider,
            ct
        );

        if (issued.IsSuccess)
            return Result.Success(AutoIssueTenantReferralCodeOutcome.Issued);

        return issued.Error.Code switch
        {
            "ReferralCode.ActiveOwnerExists" => Result.Success(AutoIssueTenantReferralCodeOutcome.AlreadyHasCode),
            "ReferralProgram.NotFound"
            or "ReferralProgram.NotActive"
            or "ReferralProgram.NotStarted"
            or "ReferralProgram.Ended" => Result.Success(AutoIssueTenantReferralCodeOutcome.NoActiveProgram),
            _ => Result.Failure<AutoIssueTenantReferralCodeOutcome>(issued.Error),
        };
    }
}
