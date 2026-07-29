using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Registration.Commands;

public sealed record CompleteOnboardingRegistrationCommand(
    string Token,
    string Password,
    string OfficeName,
    string Subdomain,
    bool TermsAccepted,
    Guid TermsVersionId
);

public sealed record CompleteOnboardingRegistrationResponse(string Status, string StatusUrl);

/// <summary>
/// PayFlow (Fase 13) — UoW final del form público de registro. Idempotente por el propio estado
/// del aggregate: TenantOnboarding.StartProvisioning ya rechaza una segunda invocación una vez
/// Status pasó de RegistrationPending (ver Onboarding.InvalidState) — no hace falta un
/// Idempotency-Key adicional (mismo criterio que el resto de comandos de este módulo).
/// </summary>
public static class CompleteOnboardingRegistrationHandler
{
    public static async Task<Result<CompleteOnboardingRegistrationResponse>> Handle(
        CompleteOnboardingRegistrationCommand command,
        ITenantOnboardingRepository onboardings,
        ITermsVersionRepository termsVersions,
        IOnboardingSubdomainReservationRepository subdomainReservations,
        ISecureTokenService tokens,
        IPasswordHasher passwordHasher,
        ITokenReferenceStore passwordHashReferences,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (!command.TermsAccepted)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(
                new Error("Onboarding.TermsNotAccepted", "Terms must be accepted.")
            );

        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure<CompleteOnboardingRegistrationResponse>(
                new Error("Onboarding.InvalidToken", "The registration token is invalid.")
            );

        var hash = tokens.Hash(command.Token).ToLowerInvariant();
        var onboarding = await onboardings.GetByRegistrationTokenHashAsync(hash, ct);
        if (onboarding is null)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(
                new Error("Onboarding.InvalidToken", "The registration token is invalid.")
            );

        var passwordPolicyResult = PasswordPolicy.Validate(command.Password, onboarding.Email);
        if (passwordPolicyResult.IsFailure)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(passwordPolicyResult.Error);

        var subdomainResult = SubdomainSlug.Create(command.Subdomain);
        if (subdomainResult.IsFailure)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(subdomainResult.Error);

        var termsVersion = await termsVersions.GetByIdAsync(command.TermsVersionId, ct);
        if (termsVersion is null)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(
                new Error("TermsVersion.NotFound", "The terms version was not found.")
            );

        var nowUtc = DateTime.UtcNow;
        var isCurrent =
            termsVersion.EffectiveFromUtc <= nowUtc
            && (termsVersion.EffectiveUntilUtc is null || termsVersion.EffectiveUntilUtc > nowUtc);
        if (!isCurrent)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(
                new Error("Onboarding.TermsVersionNotCurrent", "This terms version is no longer current.")
            );

        if (string.IsNullOrWhiteSpace(termsVersion.ContentHash))
            return Result.Failure<CompleteOnboardingRegistrationResponse>(
                new Error("Onboarding.TermsContentHashMissing", "The terms version has no published content hash.")
            );

        // PayFlow (Fase 14) — el chequeo de formato de arriba (SubdomainSlug.Create) no confirma
        // que el slug esté efectivamente reservado para ESTE onboarding: sin esto, dos compradores
        // concurrentes podrían completar el registro con el mismo subdominio.
        var activeReservation = await subdomainReservations.GetActiveBySlugAsync(
            subdomainResult.Value.Value,
            nowUtc,
            ct
        );
        if (activeReservation is null || activeReservation.OnboardingId != onboarding.Id)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(
                new Error(
                    "Onboarding.SubdomainNotReserved",
                    "The subdomain must be reserved via onboarding/subdomains/check before completing registration."
                )
            );

        var startResult = onboarding.StartProvisioning(
            command.OfficeName,
            subdomainResult.Value.Value,
            termsVersion.Id,
            termsVersion.ContentHash,
            requestContext.IpAddress ?? "unknown",
            requestContext.UserAgent ?? "unknown",
            nowUtc
        );
        if (startResult.IsFailure)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(startResult.Error);

        var consumeResult = onboarding.ConsumeRegistrationToken(nowUtc);
        if (consumeResult.IsFailure)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(consumeResult.Error);

        var consumeReservationResult = activeReservation.Consume(nowUtc);
        if (consumeReservationResult.IsFailure)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(consumeReservationResult.Error);

        // El password nunca se persiste en TenantOnboarding ni se loguea: solo su hash PBKDF2 viaja,
        // y ni siquiera ese hash queda embebido en el evento — solo una referencia de un solo uso
        // (mismo mecanismo Redis GETDEL que el RegistrationToken de Fase 9), que la Saga (Fase 15)
        // debe canjear de inmediato al crear el TenantAdmin.
        var passwordHash = passwordHasher.Hash(command.Password);
        var passwordHashReference = await passwordHashReferences.StoreAsync(passwordHash, ct);

        await bus.PublishAsync(
            new OnboardingProvisioningStartedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                OnboardingId = onboarding.Id,
                Email = onboarding.Email,
                FirstName = onboarding.FirstName,
                LastName = onboarding.LastName,
                PlanId = onboarding.PlanId,
                OfficeName = onboarding.OfficeName!,
                RequestedSubdomain = onboarding.RequestedSubdomain!,
                TermsVersionId = termsVersion.Id,
                PasswordHashReference = passwordHashReference,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new CompleteOnboardingRegistrationResponse("Provisioning", $"/onboarding/status?token={command.Token}")
        );
    }
}
