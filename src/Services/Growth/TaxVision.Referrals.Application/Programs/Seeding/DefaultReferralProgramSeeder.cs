using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Application.Programs.Seeding;

/// <summary>
/// Siembra el programa de referidos de plataforma (tenant-to-tenant) con su política por defecto:
/// el REFERIDOR gana un % del plan que paga el referido, acreditado a su Wallet; el referido no
/// recibe nada. Sin un programa activo, emitir códigos y atribuir referidos falla con
/// <c>ReferralProgram.NotFound</c>. Corre al arrancar Growth; es idempotente por <c>ProgramCode</c> y
/// NUNCA pisa un programa existente — lo que el PlatformAdmin ajustó después (% / tope / espera) se
/// conserva entre reinicios.
/// </summary>
public static class DefaultReferralProgramSeeder
{
    public const string ProgramCode = "TENANT-REFERRALS";
    public const string ProgramName = "Programa de referidos de TaxVision";
    private const string IdempotencyKey = "seed:default-referral-program:v1";

    public static async Task SeedAsync(
        IReferralProgramRepository programs,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken ct = default
    )
    {
        // La escritura de un programa platform-owned exige el contexto de tenant de plataforma
        // (TenantRepositoryGuard). El scope del seeder es propio, así que fijarlo no contamina requests.
        tenantContext.SetTenant(PlatformTenant.Id);

        if (await programs.GetPlatformProgramByCodeAsync(ProgramCode, ct) is not null)
            return;

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var policy = ReferralProgramPolicy.TenantToTenantDefaults();
        var fingerprint = CanonicalPayloadFingerprint.Compute(
            ProgramCode,
            policy.RewardType,
            policy.RewardBasisPoints,
            policy.RewardCapCents,
            policy.RewardCurrency,
            policy.WaitingPeriodDays,
            policy.MaximumRewardsPerReferrerPerCalendarYear
        );

        var actor = PlatformTenant.Id; // actor de siembra (no vacío), igual que el seeder de códigos
        var created = ReferralProgram.Create(
            ProgramCode,
            ProgramName,
            ReferralProgramScope.Platform,
            tenantScopeId: null,
            ReferralFlowType.TenantToTenant,
            policy,
            startsAtUtc: nowUtc,
            endsAtUtc: null,
            IdempotencyKey,
            fingerprint,
            actor,
            nowUtc
        );
        if (created.IsFailure)
        {
            logger.LogWarning(
                "Default referral program skipped: {Code} - {Message}",
                created.Error.Code,
                created.Error.Message
            );
            return;
        }

        var activated = created.Value.Activate(actor, nowUtc);
        if (activated.IsFailure)
        {
            logger.LogWarning("Default referral program not activated: {Code}", activated.Error.Code);
            return;
        }

        await programs.AddAsync(created.Value, ct);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Varias réplicas arrancando a la vez: otra ganó la carrera y el índice único (scope, ProgramCode) rechazó
            // el segundo INSERT. Si el programa ya existe, es el resultado esperado; si no, el error es real.
            // (La guía DDD/EDA recomienda no depender de trabajo "al arrancar" entre réplicas; este seeder es
            // idempotente a propósito y tolera esa carrera en vez de tumbar el arranque.)
            if (await programs.GetPlatformProgramByCodeAsync(ProgramCode, CancellationToken.None) is null)
                throw;

            logger.LogInformation("Default referral program {ProgramCode} was seeded by another replica.", ProgramCode);
            return;
        }

        logger.LogInformation(
            "Seeded default referral program {ProgramCode}: {BasisPoints} bp of plan paid, cap {Cap} {Currency}, wait {Days} d.",
            ProgramCode,
            policy.RewardBasisPoints,
            policy.RewardCapCents,
            policy.RewardCurrency,
            policy.WaitingPeriodDays
        );
    }
}
