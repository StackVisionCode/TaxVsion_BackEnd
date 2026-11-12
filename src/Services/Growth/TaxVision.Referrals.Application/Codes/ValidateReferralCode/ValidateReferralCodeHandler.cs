using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Referrals.Application.Codes.ValidateReferralCode;

/// <summary>
/// Validación PÚBLICA previa al alta: ¿este código de referido sirve hoy? Responde solo un booleano —no revela el
/// dueño, el programa ni los beneficios— para que el landing avise "código inválido" sin exponer datos de nadie.
/// Un código revocado (por ejemplo, reemplazado por uno nuevo), vencido o inexistente responde lo mismo: <c>false</c>.
/// </summary>
public sealed record ValidateReferralCodeQuery(string Code);

public sealed record ValidateReferralCodeResult(bool IsActive);

public static class ValidateReferralCodeHandler
{
    private const int MaxLength = 100;

    public static async Task<Result<ValidateReferralCodeResult>> Handle(
        ValidateReferralCodeQuery query,
        IReferralProgramRepository programs,
        IReferralCodeTokenHasher hasher,
        IReferralCodeRepository codes,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var code = query.Code?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length > MaxLength)
            return Result.Success(new ValidateReferralCodeResult(false));

        var program = await programs.GetActivePlatformProgramAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (program is null || program.EnsureAcceptingAttributions(nowUtc).IsFailure)
            return Result.Success(new ValidateReferralCodeResult(false));

        var hash = hasher.Hash(code);
        if (hash.IsFailure)
            return Result.Success(new ValidateReferralCodeResult(false));

        return Result.Success(
            new ValidateReferralCodeResult(await codes.ExistsUsableByHashAsync(program.Id, hash.Value, nowUtc, ct))
        );
    }
}
