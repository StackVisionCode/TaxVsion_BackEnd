using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.SubdomainReservations;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;
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
/// <para>
/// PayFlow (auditoría F20) — <c>Handle</c> descompuesto en 4 pasos (validar forma del comando,
/// cargar+validar contexto, aplicar la transición del aggregate, persistir+publicar) para que cada
/// uno se lea de una sola vez; el flujo secuencial y los mensajes de error no cambiaron.
/// </para>
/// <para>
/// PayFlow (auditoría F28) — <c>LoadRegistrationContextAsync</c> seguía mezclando 5 validaciones
/// distintas en un solo método tras el refactor de F20. Las 2 últimas (vigencia+hash de
/// <c>TermsVersion</c>, propiedad de la reserva de subdominio) se extrajeron a
/// <c>ValidateTermsVersionIsCurrent</c>/<c>ValidateSubdomainReservation</c> — mismo orden de
/// chequeos y mismos mensajes de error, sin cambio de comportamiento.
/// </para>
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
        var shapeResult = ValidateCommandShape(command);
        if (shapeResult.IsFailure)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(shapeResult.Error);

        var nowUtc = DateTime.UtcNow;

        var contextResult = await LoadRegistrationContextAsync(
            command,
            onboardings,
            termsVersions,
            subdomainReservations,
            tokens,
            nowUtc,
            ct
        );
        if (contextResult.IsFailure)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(contextResult.Error);

        var context = contextResult.Value;

        var transitionResult = ApplyRegistrationTransition(command, context, requestContext, nowUtc);
        if (transitionResult.IsFailure)
            return Result.Failure<CompleteOnboardingRegistrationResponse>(transitionResult.Error);

        return await PersistAndPublishAsync(
            command,
            context,
            passwordHasher,
            passwordHashReferences,
            unitOfWork,
            bus,
            correlation,
            ct
        );
    }

    private static Result ValidateCommandShape(CompleteOnboardingRegistrationCommand command)
    {
        if (!command.TermsAccepted)
            return Result.Failure(new Error("Onboarding.TermsNotAccepted", "Terms must be accepted."));

        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure(new Error("Onboarding.InvalidToken", "The registration token is invalid."));

        return Result.Success();
    }

    private sealed record RegistrationContext(
        TenantOnboarding Onboarding,
        SubdomainSlug Subdomain,
        TermsVersion TermsVersion,
        string TermsContentHash,
        OnboardingSubdomainReservation ActiveReservation
    );

    private static async Task<Result<RegistrationContext>> LoadRegistrationContextAsync(
        CompleteOnboardingRegistrationCommand command,
        ITenantOnboardingRepository onboardings,
        ITermsVersionRepository termsVersions,
        IOnboardingSubdomainReservationRepository subdomainReservations,
        ISecureTokenService tokens,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var hash = tokens.Hash(command.Token).ToLowerInvariant();
        var onboarding = await onboardings.GetByRegistrationTokenHashAsync(hash, ct);
        if (onboarding is null)
            return Result.Failure<RegistrationContext>(
                new Error("Onboarding.InvalidToken", "The registration token is invalid.")
            );

        var passwordPolicyResult = PasswordPolicy.Validate(command.Password, onboarding.Email);
        if (passwordPolicyResult.IsFailure)
            return Result.Failure<RegistrationContext>(passwordPolicyResult.Error);

        var subdomainResult = SubdomainSlug.Create(command.Subdomain);
        if (subdomainResult.IsFailure)
            return Result.Failure<RegistrationContext>(subdomainResult.Error);

        var termsVersion = await termsVersions.GetByIdAsync(command.TermsVersionId, ct);
        if (termsVersion is null)
            return Result.Failure<RegistrationContext>(
                new Error("TermsVersion.NotFound", "The terms version was not found.")
            );

        var termsContentHashResult = ValidateTermsVersionIsCurrent(termsVersion, nowUtc);
        if (termsContentHashResult.IsFailure)
            return Result.Failure<RegistrationContext>(termsContentHashResult.Error);

        var activeReservationResult = await ValidateSubdomainReservationAsync(
            subdomainReservations,
            subdomainResult.Value,
            onboarding.Id,
            nowUtc,
            ct
        );
        if (activeReservationResult.IsFailure)
            return Result.Failure<RegistrationContext>(activeReservationResult.Error);

        return Result.Success(
            new RegistrationContext(
                onboarding,
                subdomainResult.Value,
                termsVersion,
                termsContentHashResult.Value,
                activeReservationResult.Value
            )
        );
    }

    private static Result<string> ValidateTermsVersionIsCurrent(TermsVersion termsVersion, DateTime nowUtc)
    {
        var isCurrent =
            termsVersion.EffectiveFromUtc <= nowUtc
            && (termsVersion.EffectiveUntilUtc is null || termsVersion.EffectiveUntilUtc > nowUtc);
        if (!isCurrent)
            return Result.Failure<string>(
                new Error("Onboarding.TermsVersionNotCurrent", "This terms version is no longer current.")
            );

        if (string.IsNullOrWhiteSpace(termsVersion.ContentHash))
            return Result.Failure<string>(
                new Error("Onboarding.TermsContentHashMissing", "The terms version has no published content hash.")
            );

        return Result.Success(termsVersion.ContentHash);
    }

    // PayFlow (Fase 14) — el chequeo de formato en LoadRegistrationContextAsync (SubdomainSlug.Create)
    // no confirma que el slug esté efectivamente reservado para ESTE onboarding: sin esto, dos
    // compradores concurrentes podrían completar el registro con el mismo subdominio.
    private static async Task<Result<OnboardingSubdomainReservation>> ValidateSubdomainReservationAsync(
        IOnboardingSubdomainReservationRepository subdomainReservations,
        SubdomainSlug subdomain,
        Guid onboardingId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var activeReservation = await subdomainReservations.GetActiveBySlugAsync(subdomain.Value, nowUtc, ct);
        if (activeReservation is null || activeReservation.OnboardingId != onboardingId)
            return Result.Failure<OnboardingSubdomainReservation>(
                new Error(
                    "Onboarding.SubdomainNotReserved",
                    "The subdomain must be reserved via onboarding/subdomains/check before completing registration."
                )
            );

        return Result.Success(activeReservation);
    }

    private static Result ApplyRegistrationTransition(
        CompleteOnboardingRegistrationCommand command,
        RegistrationContext context,
        IRequestContext requestContext,
        DateTime nowUtc
    )
    {
        var startResult = context.Onboarding.StartProvisioning(
            command.OfficeName,
            context.Subdomain.Value,
            context.TermsVersion.Id,
            context.TermsContentHash,
            requestContext.IpAddress ?? "unknown",
            requestContext.UserAgent ?? "unknown",
            nowUtc
        );
        if (startResult.IsFailure)
            return startResult;

        var consumeResult = context.Onboarding.ConsumeRegistrationToken(nowUtc);
        if (consumeResult.IsFailure)
            return consumeResult;

        return context.ActiveReservation.Consume(nowUtc);
    }

    private static async Task<Result<CompleteOnboardingRegistrationResponse>> PersistAndPublishAsync(
        CompleteOnboardingRegistrationCommand command,
        RegistrationContext context,
        IPasswordHasher passwordHasher,
        ITokenReferenceStore passwordHashReferences,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var onboarding = context.Onboarding;

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
                BillingCycle = onboarding.BillingCycle,
                OfficeName = onboarding.OfficeName!,
                RequestedSubdomain = onboarding.RequestedSubdomain!,
                TermsVersionId = context.TermsVersion.Id,
                PasswordHashReference = passwordHashReference,
                PaymentCompletedAtUtc = onboarding.PaymentCompletedAtUtc!.Value,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new CompleteOnboardingRegistrationResponse("Provisioning", $"/onboarding/status?token={command.Token}")
        );
    }
}
