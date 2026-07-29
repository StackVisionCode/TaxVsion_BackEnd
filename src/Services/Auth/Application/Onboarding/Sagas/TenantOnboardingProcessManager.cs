using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Failures;
using TaxVision.Auth.Application.Onboarding.Sagas.Commands;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Sagas;

/// <summary>
/// PayFlow (Fase 15) — orquestador Wolverine (<see cref="Saga"/>) de los 6 pasos remotos de
/// provisioning post-pago: Tenant → TenantAdmin → Subscription → CloudStorage → Subdomain →
/// Defaults. Primer <c>Saga</c> del monorepo — persistido vía EF Core en <c>AuthDbContext</c> (mapeo
/// estándar de <c>OnboardingSagaConfiguration</c>, sin wiring especial de Wolverine más allá de
/// <c>UseEntityFrameworkCoreTransactions</c>, ya configurado).
/// <para>
/// <see cref="Id"/> ES el <c>OnboardingId</c> — no un identificador propio de la saga. El estado de
/// negocio real vive en <see cref="TenantOnboarding"/> (fuente de verdad para
/// <c>/onboarding/status</c>); este objeto solo guarda lo mínimo para reconstruir los comandos
/// salientes entre reinicios del proceso: <see cref="TenantId"/>/<see cref="UserId"/>/
/// <see cref="SubscriptionId"/> se van poblando a medida que cada paso M2M confirma, y
/// <see cref="PasswordHashReference"/> se destruye (se pone a null) apenas se usa en
/// <see cref="Handle(TenantCreatedForOnboardingIntegrationEvent)"/> — nunca sobrevive más de un paso.
/// </para>
/// <para>
/// Sobre fallos: <see cref="Handle(OnboardingProvisioningStepFailedIntegrationEvent, ITenantOnboardingRepository, IUnitOfWork, CancellationToken)"/>
/// registra el fallo en el aggregate pero deliberadamente NO llama <c>MarkCompleted()</c> — la saga
/// de Wolverine permanece viva, a la espera de un futuro comando de resume (Fase 17:
/// <c>FailureClassifier</c> + retry + <c>OnboardingAdminController</c>). Clasificación
/// transient/permanent y reintentos automáticos quedan fuera de esta fase.
/// </para>
/// </summary>
public sealed class TenantOnboardingProcessManager : Saga
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public Guid? PasswordHashReference { get; set; }
    public string Email { get; set; } = default!;
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public Guid PlanId { get; set; }

    public static (TenantOnboardingProcessManager, CreateTenantForOnboardingCommand) Start(
        OnboardingProvisioningStartedIntegrationEvent evt
    )
    {
        var saga = new TenantOnboardingProcessManager
        {
            Id = evt.OnboardingId,
            Email = evt.Email,
            FirstName = evt.FirstName,
            LastName = evt.LastName,
            PlanId = evt.PlanId,
            PasswordHashReference = evt.PasswordHashReference,
        };

        var command = new CreateTenantForOnboardingCommand(
            evt.OnboardingId,
            evt.OfficeName,
            evt.RequestedSubdomain,
            evt.Email
        );
        return (saga, command);
    }

    public async Task<CreateTenantOwnerCommand?> Handle(
        TenantCreatedForOnboardingIntegrationEvent evt,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        TenantId = evt.CreatedTenantId;

        var onboarding = await onboardings.GetByIdAsync(Id, ct);
        if (onboarding is null)
            return null;

        var result = onboarding.SetTenantCreated(evt.CreatedTenantId);
        if (result.IsFailure)
            return null;

        await unitOfWork.SaveChangesAsync(ct);

        var command = new CreateTenantOwnerCommand(
            Id,
            TenantId.Value,
            Email,
            FirstName,
            LastName,
            PasswordHashReference!.Value
        );
        PasswordHashReference = null;
        return command;
    }

    public async Task<ActivateSubscriptionCommand?> Handle(
        TenantOwnerCreatedIntegrationEvent evt,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        UserId = evt.CreatedUserId;

        var onboarding = await onboardings.GetByIdAsync(Id, ct);
        if (onboarding is null)
            return null;

        var result = onboarding.SetTenantAdminCreated(evt.CreatedUserId);
        if (result.IsFailure)
            return null;

        await unitOfWork.SaveChangesAsync(ct);

        return new ActivateSubscriptionCommand(Id, TenantId!.Value, PlanId);
    }

    public async Task<ProvisionStorageForTenantCommand?> Handle(
        SubscriptionActivatedForOnboardingIntegrationEvent evt,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        SubscriptionId = evt.CreatedSubscriptionId;

        var onboarding = await onboardings.GetByIdAsync(Id, ct);
        if (onboarding is null)
            return null;

        var result = onboarding.SetSubscriptionActivated(evt.CreatedSubscriptionId);
        if (result.IsFailure)
            return null;

        await unitOfWork.SaveChangesAsync(ct);

        return new ProvisionStorageForTenantCommand(Id, TenantId!.Value, UserId!.Value, SubscriptionId.Value);
    }

    /// <summary>PayFlow (Fase 17) — al registrar el fallo, clasifica de inmediato con
    /// <see cref="FailureClassifier"/>: un fallo Permanent (incluye SIEMPRE el paso TenantAdmin, ver
    /// doc-comment del classifier) salta directo a ManualReview sin esperar al retry scheduler — "sin
    /// retry, ManualReview inmediato" (plan Fase 17). Un fallo Transient queda en ProvisioningFailed
    /// para que <c>OnboardingRetryScheduler</c> lo reintente con cadencia escalonada.</summary>
    public async Task Handle(
        OnboardingProvisioningStepFailedIntegrationEvent evt,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        IOnboardingMetrics metrics,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(Id, ct);
        if (onboarding is null)
            return;

        if (!Enum.TryParse<TenantProvisioningStep>(evt.FailedStep, out var step))
            return;

        if (onboarding.MarkProvisioningFailed(step, evt.FailureCode, evt.FailureReason).IsFailure)
            return;

        metrics.RecordFailed(step.ToString());

        if (FailureClassifier.Classify(step, evt.FailureCode) == FailureKind.Permanent)
        {
            onboarding.MarkManualReview($"Permanent failure at step {step}: {evt.FailureReason}");
            metrics.RecordManualReview();
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>PayFlow (Fase 17) — dispara el admin (resume/update-and-resume) o el retry scheduler
    /// automático para un fallo Transient. Solo reconstruye Tenant/Subscription — nunca TenantAdmin
    /// (ver <see cref="FailureClassifier"/>, siempre Permanent, nunca llega acá con ese paso).
    /// Usa el estado propio de la Saga (Email/OfficeName/etc, poblados desde <see cref="Start"/>) para
    /// reconstruir el comando exacto que falló — <see cref="TenantOnboarding"/> es la fuente de verdad
    /// para <c>RequestedSubdomain</c>/<c>PlanId</c> porque <c>UpdateProvisioningInputs</c> (admin) los
    /// corrige ahí, no en la Saga.</summary>
    public async Task<object?> Handle(
        ResumeOnboardingProvisioningCommand command,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(Id, ct);
        if (onboarding is null)
            return null;

        var failedStep = onboarding.FailedStep;
        if (failedStep is not (TenantProvisioningStep.Tenant or TenantProvisioningStep.Subscription))
            return null;

        if (onboarding.ResumeProvisioning().IsFailure)
            return null;

        await unitOfWork.SaveChangesAsync(ct);

        return failedStep switch
        {
            TenantProvisioningStep.Tenant => new CreateTenantForOnboardingCommand(
                Id,
                onboarding.OfficeName!,
                onboarding.RequestedSubdomain!,
                Email
            ),
            TenantProvisioningStep.Subscription => new ActivateSubscriptionCommand(
                Id,
                TenantId!.Value,
                onboarding.PlanId
            ),
            _ => null,
        };
    }

    public void Handle(TenantOnboardingCompletedIntegrationEvent evt) => MarkCompleted();

    public static void NotFound(TenantCreatedForOnboardingIntegrationEvent evt) { }

    public static void NotFound(TenantOwnerCreatedIntegrationEvent evt) { }

    public static void NotFound(SubscriptionActivatedForOnboardingIntegrationEvent evt) { }

    public static void NotFound(OnboardingProvisioningStepFailedIntegrationEvent evt) { }

    public static void NotFound(ResumeOnboardingProvisioningCommand command) { }

    public static void NotFound(TenantOnboardingCompletedIntegrationEvent evt) { }
}
