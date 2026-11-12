using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Codes.IssueTenantReferralCode;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Participants;

namespace TaxVision.Referrals.Application.Codes.RegenerateTenantReferralCode;

public static class RegenerateTenantReferralCodeHandler
{
    private const string Operation = "Referrals.RegenerateTenantReferralCode.v1";
    private const int DisplayPrefixLength = 8;
    private const int DefaultValidityYears = 5;

    public static async Task<Result<IssueTenantReferralCodeResult>> Handle(
        RegenerateTenantReferralCodeCommand command,
        IReferralProgramRepository programs,
        IReferralCodeRepository codes,
        IReferralCodeTokenGenerator tokenGenerator,
        IReferralCodeTokenHasher tokenHasher,
        IReferralIdempotencyExecutor idempotency,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<IssueTenantReferralCodeResult>(actor.Error);

        if (command.TenantId == Guid.Empty)
            return Failure("ReferralCode.InvalidScope", "TenantId is required.");

        if (command.ExpiresAtUtc is { Kind: not DateTimeKind.Utc })
            return Failure("ReferralCode.InvalidUtcExpiry", "ExpiresAtUtc must use DateTimeKind.Utc.");

        var program = await programs.GetActivePlatformProgramAsync(ct);
        if (program is null)
            return Failure("ReferralProgram.NotFound", "There is no active referral program.");

        var generated = tokenGenerator.Generate(program.Id, command.TenantId, command.IdempotencyKey);
        if (generated.IsFailure)
            return Result.Failure<IssueTenantReferralCodeResult>(generated.Error);

        var clearText = generated.Value.Reveal();
        var codeHash = tokenHasher.Hash(clearText);
        if (codeHash.IsFailure)
            return Result.Failure<IssueTenantReferralCodeResult>(codeHash.Error);

        // La expiración depende de "ahora" y no entra en la huella: un replay con la misma key sigue siendo el mismo pedido.
        var fingerprint = CanonicalPayloadFingerprint.Compute(
            command.TenantId,
            program.Id,
            codeHash.Value,
            command.ActorUserId
        );

        return await idempotency.ExecuteAsync(
            command.TenantId,
            Operation,
            program.Id,
            command.IdempotencyKey,
            fingerprint,
            async operationCt =>
            {
                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var expiresAtUtc = command.ExpiresAtUtc ?? nowUtc.AddYears(DefaultValidityYears);
                if (program.EndsAtUtc is { } programEnd && expiresAtUtc > programEnd)
                    expiresAtUtc = programEnd;

                var existing = await codes.GetActiveOwnedAsync(
                    command.TenantId,
                    program.Id,
                    ReferralParticipantType.Tenant,
                    command.TenantId,
                    operationCt
                );
                if (existing is not null)
                {
                    var superseded = existing.Supersede(command.ActorUserId, nowUtc);
                    if (superseded.IsFailure)
                        return Result.Failure<IssueTenantReferralCodeResult>(superseded.Error);

                    // La revocación debe confirmarse antes de insertar el código nuevo: el índice único del código
                    // activo es filtrado (Status = Active) y EF no puede ordenar el UPDATE antes del INSERT por sí solo.
                    // Sigue dentro de la transacción del ejecutor de idempotencia, así que no queda a medias.
                    await unitOfWork.SaveChangesAsync(operationCt);
                }

                var created = ReferralCode.Create(
                    program,
                    ReferralParticipantType.Tenant,
                    command.TenantId,
                    codeHash.Value,
                    clearText[..Math.Min(DisplayPrefixLength, clearText.Length)],
                    clearText[^4..],
                    expiresAtUtc,
                    command.IdempotencyKey,
                    fingerprint,
                    command.ActorUserId,
                    nowUtc,
                    command.ActorUserId
                );
                if (created.IsFailure)
                    return Result.Failure<IssueTenantReferralCodeResult>(created.Error);

                await codes.AddAsync(created.Value, operationCt);
                return Result.Success(IssueTenantReferralCodeResult.From(created.Value));
            },
            ct
        );
    }

    private static Result<IssueTenantReferralCodeResult> Failure(string code, string message) =>
        Result.Failure<IssueTenantReferralCodeResult>(new Error(code, message));
}
