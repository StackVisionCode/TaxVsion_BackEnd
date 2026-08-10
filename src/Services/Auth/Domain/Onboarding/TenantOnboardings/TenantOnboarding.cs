using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;

namespace TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

/// <summary>
/// Proceso de alta pago-primero (PayFlow): selección → pago → registro → provisioning → tenant
/// operativo. No es tenant-owned (hereda <see cref="BaseEntity"/>, no <c>AggregateRoot</c>): existe
/// antes de que el tenant real exista, así que el <see cref="TenantId"/> que guarda es solo la
/// referencia de negocio al tenant creado — no el discriminador de multi-tenencia — y por eso no
/// participa del filtro global fail-closed ni del drenado de domain events de AuthDbContext. Los
/// eventos de integración cross-servicio (OnboardingPaymentSucceeded, TenantOnboardingCompleted,
/// etc.) los publican explícitamente los handlers de Application en las fases 8/9/12/13/15 del
/// plan, igual que ya hace <c>SetRolePermissionsHandler</c> con <c>RolePermissionsChangedIntegrationEvent</c>.
/// <para>
/// Auditoría F10 — implicación práctica de lo anterior: esta clase NO soporta
/// <c>AggregateRoot.AddDomainEvent</c>, así que no hay forma de emitir un domain event *interno*
/// (in-process, sin cruzar servicios) desde una transición de estado. Si algún día hace falta uno
/// (p.ej. para actualizar una proyección local en Auth cuando cambia el status), hay que publicarlo
/// explícitamente en el handler de Application que invoca la transición — mismo patrón que ya usan
/// los integration events de arriba, ver <c>CompleteOnboardingRegistrationHandler</c> como ejemplo.
/// </para>
/// </summary>
public sealed class TenantOnboarding : BaseEntity
{
    private readonly List<OnboardingCodeReservation> _codeReservations = [];

    private TenantOnboarding() { }

    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public DateTime EmailVerifiedAtUtc { get; private set; }
    public string? Phone { get; private set; }
    public Guid PlanId { get; private set; }

    /// <summary>Ciclo de facturación elegido por el comprador ("Monthly"/"Yearly"). Auth es passthrough:
    /// no interpreta el valor; lo lleva a Subscription (pricing + activación) y PaymentApp (cobro), que
    /// lo parsean. Default "Monthly".</summary>
    public string BillingCycle { get; private set; } = "Monthly";

    public TenantOnboardingStatus Status { get; private set; }

    public Guid? PaymentId { get; private set; }
    public string? PaymentStatus { get; private set; }
    public string? PaymentReference { get; private set; }
    public DateTime? PaymentCompletedAtUtc { get; private set; }

    // Gift/Referral: desglose comercial calculado por la reserva secuencial (apilada) en Growth.
    // Se congela antes del checkout. Null hasta que se aplica al menos un código; FullyCovered = neto 0.
    public Guid? ReferralAttributionId { get; private set; }
    public long? GrossAmountCents { get; private set; }
    public long? TotalDiscountCents { get; private set; }
    public long? NetAmountCents { get; private set; }
    public string? Currency { get; private set; }
    public bool FullyCovered { get; private set; }
    public IReadOnlyCollection<OnboardingCodeReservation> CodeReservations => _codeReservations;

    public string? RegistrationTokenHash { get; private set; }
    public DateTime? RegistrationTokenExpiresAtUtc { get; private set; }
    public DateTime? RegistrationTokenUsedAtUtc { get; private set; }

    /// <summary>PayFlow (Fase 11) — FileId del recibo PDF en CloudStorage, guardado bajo
    /// <c>PlatformTenant.Id</c> (Documents Fase 10). Poblado por OnboardingReceiptGenerationCompletedConsumer.</summary>
    public Guid? ReceiptFileId { get; private set; }

    public string? OfficeName { get; private set; }
    public string? RequestedSubdomain { get; private set; }

    public Guid? TermsVersionId { get; private set; }
    public string? TermsContentHash { get; private set; }
    public DateTime? TermsAcceptedAtUtc { get; private set; }
    public string? AcceptedFromIp { get; private set; }
    public string? UserAgent { get; private set; }

    /// <summary>Referencia de negocio al Tenant creado por la Saga — NO el discriminador multi-tenant.</summary>
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? SubscriptionId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ProvisioningStartedAtUtc { get; private set; }
    public DateTime? RegistrationCompletedAtUtc { get; private set; }

    public TenantProvisioningStep CurrentStep { get; private set; } = TenantProvisioningStep.None;
    public TenantProvisioningStep? FailedStep { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>PayFlow (Fase 17) — cuenta los reintentos automáticos ya intentados para el fallo
    /// actual (se resetea a 0 en cada <see cref="ResumeProvisioning"/> exitoso). El poller de retry
    /// la usa para la cadencia escalonada 5min/15min/1h hasta 24h.</summary>
    public int RetryAttempt { get; private set; }

    /// <summary>PayFlow (Fase 17) — próximo intento de reintento automático programado por
    /// <c>OnboardingRetryScheduler</c>. Null si no hay reintento pendiente (fallo permanente, o
    /// esperando acción manual).</summary>
    public DateTime? NextRetryAtUtc { get; private set; }

    public static Result<TenantOnboarding> Create(
        string email,
        DateTime emailVerifiedAtUtc,
        Guid planId,
        string firstName,
        string lastName,
        string? phone,
        DateTime nowUtc,
        string? billingCycle = null
    )
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedEmail.Length == 0 || !normalizedEmail.Contains('@'))
            return Result.Failure<TenantOnboarding>(new Error("Onboarding.Email", "A valid email is required."));

        if (planId == Guid.Empty)
            return Result.Failure<TenantOnboarding>(new Error("Onboarding.Plan", "A plan is required."));

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return Result.Failure<TenantOnboarding>(
                new Error("Onboarding.Name", "First name and last name are required.")
            );

        return Result.Success(
            new TenantOnboarding
            {
                Email = normalizedEmail,
                EmailVerifiedAtUtc = emailVerifiedAtUtc,
                PlanId = planId,
                BillingCycle = string.IsNullOrWhiteSpace(billingCycle) ? "Monthly" : billingCycle.Trim(),
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                Status = TenantOnboardingStatus.PendingPayment,
                CurrentStep = TenantProvisioningStep.None,
                CreatedAtUtc = nowUtc,
            }
        );
    }

    public Result MarkPaymentProcessing(Guid paymentId, string paymentReference)
    {
        if (paymentId == Guid.Empty || string.IsNullOrWhiteSpace(paymentReference))
            return Result.Failure(new Error("Onboarding.PaymentReference", "A payment id and reference are required."));

        if (Status == TenantOnboardingStatus.PaymentProcessing && PaymentId == paymentId)
            return Result.Success();

        if (Status != TenantOnboardingStatus.PendingPayment)
            return Result.Failure(InvalidTransition());

        PaymentId = paymentId;
        PaymentReference = paymentReference;
        PaymentStatus = "Processing";
        Status = TenantOnboardingStatus.PaymentProcessing;
        return Result.Success();
    }

    /// <summary>Gift/Referral: congela el desglose comercial (bruto/descuento/neto) y las reservas de
    /// código apiladas ANTES del checkout. Válido solo en PendingPayment. Idempotente: si ya se aplicó
    /// (hay reservas), es no-op — un reintento del checkout no re-reserva. <c>FullyCovered = neto 0</c>.</summary>
    public Result ApplyOnboardingPricing(
        long grossCents,
        long totalDiscountCents,
        long netCents,
        string currency,
        Guid? referralAttributionId,
        IReadOnlyList<OnboardingCodeReservationInput> reservations,
        DateTime nowUtc
    )
    {
        if (Status != TenantOnboardingStatus.PendingPayment)
            return Result.Failure(InvalidTransition());

        if (_codeReservations.Count > 0)
            return Result.Success(); // Ya aplicado (replay del checkout); no re-reservar.

        if (grossCents < 0 || totalDiscountCents < 0 || netCents < 0)
            return Result.Failure(new Error("Onboarding.InvalidAmount", "Amounts cannot be negative."));
        if (totalDiscountCents > grossCents)
            return Result.Failure(new Error("Onboarding.DiscountExceedsGross", "Discount cannot exceed gross."));
        if (grossCents - totalDiscountCents != netCents)
            return Result.Failure(new Error("Onboarding.InvalidNet", "Net must equal gross minus discount."));

        var sumReservations = reservations?.Sum(r => r.DiscountCents) ?? 0;
        if (sumReservations != totalDiscountCents)
            return Result.Failure(
                new Error("Onboarding.AdjustmentMismatch", "Sum of reservations must equal the total discount.")
            );

        GrossAmountCents = grossCents;
        TotalDiscountCents = totalDiscountCents;
        NetAmountCents = netCents;
        Currency = currency;
        ReferralAttributionId = referralAttributionId;
        FullyCovered = netCents == 0;

        var order = 0;
        foreach (var r in reservations ?? [])
            _codeReservations.Add(
                new OnboardingCodeReservation(
                    Id,
                    r.CodeReservationId,
                    r.BenefitType,
                    r.Code,
                    r.DiscountCents,
                    r.SnapshotHash,
                    order++,
                    nowUtc
                )
            );

        return Result.Success();
    }

    /// <summary>Carril $0 (cubierto 100% por código): pasa de PendingPayment a PaymentCompleted SIN pago
    /// (no PaymentApp, no Stripe). Requiere haber aplicado un pricing con neto 0. Idempotente.</summary>
    public Result MarkFullyCoveredByCode(DateTime nowUtc)
    {
        if (Status == TenantOnboardingStatus.PaymentCompleted && FullyCovered)
            return Result.Success();

        if (Status != TenantOnboardingStatus.PendingPayment)
            return Result.Failure(InvalidTransition());

        if (!FullyCovered || NetAmountCents is not 0)
            return Result.Failure(
                new Error("Onboarding.NotFullyCovered", "The onboarding is not fully covered by a code.")
            );

        PaymentStatus = "CoveredByCode";
        PaymentCompletedAtUtc = nowUtc;
        Status = TenantOnboardingStatus.PaymentCompleted;
        return Result.Success();
    }

    public Result MarkPaymentCompleted(string paymentReference, DateTime paidAtUtc)
    {
        if (string.IsNullOrWhiteSpace(paymentReference))
            return Result.Failure(new Error("Onboarding.PaymentReference", "A payment reference is required."));

        if (Status == TenantOnboardingStatus.PaymentCompleted && PaymentReference == paymentReference)
            return Result.Success();

        if (Status != TenantOnboardingStatus.PaymentProcessing)
            return Result.Failure(InvalidTransition());

        if (paymentReference != PaymentReference)
            return Result.Failure(
                new Error(
                    "Onboarding.PaymentReferenceMismatch",
                    "Payment reference does not match the pending checkout."
                )
            );

        PaymentStatus = "Succeeded";
        PaymentCompletedAtUtc = paidAtUtc;
        Status = TenantOnboardingStatus.PaymentCompleted;
        return Result.Success();
    }

    public Result MarkPaymentFailed(string reason)
    {
        if (Status != TenantOnboardingStatus.PaymentProcessing)
            return Result.Failure(InvalidTransition());

        PaymentStatus = "Failed";
        FailureReason = reason;
        Status = TenantOnboardingStatus.PaymentFailed;
        return Result.Success();
    }

    public Result SetRegistrationToken(RegistrationTokenHash hash, DateTime expiresAtUtc)
    {
        if (Status != TenantOnboardingStatus.PaymentCompleted)
            return Result.Failure(InvalidTransition());

        RegistrationTokenHash = hash.Value;
        RegistrationTokenExpiresAtUtc = expiresAtUtc;
        Status = TenantOnboardingStatus.RegistrationPending;
        return Result.Success();
    }

    /// <summary>PayFlow (Fase 11) — idempotente: un replay del mismo FileId (reentrega del evento
    /// DocumentGenerationCompleted) no falla. No hay restricción de Status: el recibo puede llegar
    /// en cualquier punto posterior a PaymentCompleted.</summary>
    public Result SetReceiptFileId(Guid fileId)
    {
        if (fileId == Guid.Empty)
            return Result.Failure(new Error("Onboarding.ReceiptFileId", "A receipt file id is required."));

        if (ReceiptFileId == fileId)
            return Result.Success();

        ReceiptFileId = fileId;
        return Result.Success();
    }

    public Result StartProvisioning(
        string officeName,
        string requestedSubdomain,
        Guid termsVersionId,
        string termsContentHash,
        string acceptedFromIp,
        string userAgent,
        DateTime nowUtc
    )
    {
        if (Status != TenantOnboardingStatus.RegistrationPending)
            return Result.Failure(InvalidTransition());

        if (RegistrationTokenUsedAtUtc is not null)
            return Result.Failure(new Error("Onboarding.TokenUsed", "The registration token was already used."));

        if (RegistrationTokenExpiresAtUtc is null || nowUtc >= RegistrationTokenExpiresAtUtc)
            return Result.Failure(new Error("Onboarding.TokenExpired", "The registration token has expired."));

        if (string.IsNullOrWhiteSpace(officeName) || string.IsNullOrWhiteSpace(requestedSubdomain))
            return Result.Failure(
                new Error("Onboarding.RegistrationDetails", "Office name and subdomain are required.")
            );

        OfficeName = officeName.Trim();
        RequestedSubdomain = requestedSubdomain.Trim();
        TermsVersionId = termsVersionId;
        TermsContentHash = termsContentHash;
        TermsAcceptedAtUtc = nowUtc;
        AcceptedFromIp = acceptedFromIp;
        UserAgent = userAgent;
        ProvisioningStartedAtUtc = nowUtc;
        Status = TenantOnboardingStatus.Provisioning;
        CurrentStep = TenantProvisioningStep.Tenant;
        return Result.Success();
    }

    public Result SetTenantCreated(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(new Error("Onboarding.TenantId", "A tenant id is required."));

        if (TenantId == tenantId)
            return Result.Success();

        if (!IsProvisioningAtStep(TenantProvisioningStep.Tenant))
            return Result.Failure(InvalidTransition());

        TenantId = tenantId;
        CurrentStep = TenantProvisioningStep.TenantAdmin;
        return Result.Success();
    }

    public Result SetTenantAdminCreated(Guid userId)
    {
        if (userId == Guid.Empty)
            return Result.Failure(new Error("Onboarding.UserId", "A user id is required."));

        if (UserId == userId)
            return Result.Success();

        if (!IsProvisioningAtStep(TenantProvisioningStep.TenantAdmin))
            return Result.Failure(InvalidTransition());

        UserId = userId;
        CurrentStep = TenantProvisioningStep.Subscription;
        return Result.Success();
    }

    public Result SetSubscriptionActivated(Guid subscriptionId)
    {
        if (subscriptionId == Guid.Empty)
            return Result.Failure(new Error("Onboarding.SubscriptionId", "A subscription id is required."));

        if (SubscriptionId == subscriptionId)
            return Result.Success();

        if (!IsProvisioningAtStep(TenantProvisioningStep.Subscription))
            return Result.Failure(InvalidTransition());

        SubscriptionId = subscriptionId;
        CurrentStep = TenantProvisioningStep.CloudStorage;
        return Result.Success();
    }

    /// <summary>Avanza los pasos sin identidad propia (CloudStorage/Subdomain/Defaults).</summary>
    public Result MarkStepCompleted(TenantProvisioningStep step)
    {
        if (step is TenantProvisioningStep.None or TenantProvisioningStep.Completed)
            return Result.Failure(
                new Error("Onboarding.InvalidStep", "That step cannot be marked completed directly.")
            );

        var next = NextStep(step);
        if (CurrentStep == next)
            return Result.Success();

        if (!IsProvisioningAtStep(step))
            return Result.Failure(InvalidTransition());

        CurrentStep = next;
        return Result.Success();
    }

    public Result MarkProvisioningFailed(TenantProvisioningStep failedStep, string failureCode, string failureReason)
    {
        if (Status != TenantOnboardingStatus.Provisioning)
            return Result.Failure(InvalidTransition());

        FailedStep = failedStep;
        FailureCode = failureCode;
        FailureReason = failureReason;
        NextRetryAtUtc = null;
        Status = TenantOnboardingStatus.ProvisioningFailed;
        return Result.Success();
    }

    /// <summary>PayFlow (Fase 17) — programa el próximo reintento automático de un fallo transient.
    /// Solo válido mientras el fallo sigue vivo (ProvisioningFailed); no cambia el Status.</summary>
    public Result ScheduleRetry(DateTime nextRetryAtUtc)
    {
        if (Status != TenantOnboardingStatus.ProvisioningFailed)
            return Result.Failure(InvalidTransition());

        RetryAttempt++;
        NextRetryAtUtc = nextRetryAtUtc;
        return Result.Success();
    }

    /// <summary>Deja el onboarding "limpio" (borra FailedStep/FailureCode/FailureReason) mientras el
    /// paso reintentado está en vuelo. A propósito NO toca <see cref="RetryAttempt"/>/
    /// <see cref="NextRetryAtUtc"/>: si el reintento vuelve a fallar,
    /// <see cref="MarkProvisioningFailed"/> corre otra vez y <c>OnboardingRetryScheduler</c> necesita
    /// que <see cref="RetryAttempt"/> siga acumulando para la cadencia escalonada (5min/15min/1h) —
    /// resetearlo acá lo dejaría reintentando cada 5 minutos para siempre. Para un resume manual del
    /// admin (fresh start real), el caller debe llamar <see cref="ResetRetryState"/> antes.</summary>
    public Result ResumeProvisioning()
    {
        // ManualReview también puede resumirse: es el mismo fallo que ProvisioningFailed, solo que
        // ya fue derivado a un humano (por FailureClassifier Permanent, o por el retry scheduler tras
        // agotar los 3 intentos). El admin decide si de verdad ya no aplica (force-complete/refund) o
        // si corrigió lo que hacía falta y puede reintentar.
        if (Status is not (TenantOnboardingStatus.ProvisioningFailed or TenantOnboardingStatus.ManualReview))
            return Result.Failure(InvalidTransition());

        FailedStep = null;
        FailureCode = null;
        FailureReason = null;
        Status = TenantOnboardingStatus.Provisioning;
        return Result.Success();
    }

    /// <summary>PayFlow (Fase 17) — acción explícita del admin (resume/update-and-resume manual):
    /// descarta el conteo de reintentos automáticos acumulado, como si el fallo fuera nuevo. Se llama
    /// ANTES de <see cref="ResumeProvisioning"/> en el flujo admin — nunca desde el retry scheduler
    /// automático.</summary>
    public Result ResetRetryState()
    {
        if (Status is not (TenantOnboardingStatus.ProvisioningFailed or TenantOnboardingStatus.ManualReview))
            return Result.Failure(InvalidTransition());

        RetryAttempt = 0;
        NextRetryAtUtc = null;
        return Result.Success();
    }

    /// <summary>PayFlow (Fase 17) — corrige el subdominio y/o plan antes de reintentar un paso
    /// Tenant/Subscription cuyo fallo fue causado por un dato de entrada inválido (subdominio ya
    /// tomado, plan despublicado). Solo válido mientras el fallo sigue vivo; no avanza el Status —
    /// el caller debe llamar <see cref="ResumeProvisioning"/> después.</summary>
    public Result UpdateProvisioningInputs(string? subdomain, Guid? planId)
    {
        if (Status is not (TenantOnboardingStatus.ProvisioningFailed or TenantOnboardingStatus.ManualReview))
            return Result.Failure(InvalidTransition());

        if (subdomain is not null)
        {
            if (string.IsNullOrWhiteSpace(subdomain))
                return Result.Failure(new Error("Onboarding.RegistrationDetails", "Subdomain cannot be blank."));

            RequestedSubdomain = subdomain.Trim();
        }

        if (planId is not null)
        {
            if (planId == Guid.Empty)
                return Result.Failure(new Error("Onboarding.Plan", "A plan is required."));

            PlanId = planId.Value;
        }

        return Result.Success();
    }

    public Result MarkManualReview(string reason)
    {
        if (Status != TenantOnboardingStatus.ProvisioningFailed)
            return Result.Failure(InvalidTransition());

        FailureReason = reason;
        NextRetryAtUtc = null;
        Status = TenantOnboardingStatus.ManualReview;
        return Result.Success();
    }

    /// <summary>PayFlow (Fase 17) — cierre administrativo excepcional: el operador confirma que
    /// Tenant/TenantAdmin/Subscription ya existen (los 3 pasos con identidad real) y que los pasos
    /// restantes (CloudStorage/Subdomain/Defaults, ninguno M2M — ver doc-comments de sus handlers)
    /// no ameritan seguir bloqueando el onboarding. A diferencia de <see cref="MarkCompleted"/> no
    /// exige <c>CurrentStep == Completed</c> — por eso es una acción administrativa explícita, nunca
    /// automática.</summary>
    public Result AdminForceComplete(string reason, DateTime nowUtc)
    {
        if (Status is not (TenantOnboardingStatus.ProvisioningFailed or TenantOnboardingStatus.ManualReview))
            return Result.Failure(InvalidTransition());

        if (TenantId is null || UserId is null || SubscriptionId is null)
        {
            return Result.Failure(
                new Error(
                    "Onboarding.ForceCompleteIncomplete",
                    "Cannot force-complete: Tenant, TenantAdmin, and Subscription must all exist first."
                )
            );
        }

        FailureReason = reason;
        NextRetryAtUtc = null;
        CurrentStep = TenantProvisioningStep.Completed;
        RegistrationCompletedAtUtc = nowUtc;
        Status = TenantOnboardingStatus.Completed;
        return Result.Success();
    }

    public Result MarkCompleted(DateTime nowUtc)
    {
        if (Status == TenantOnboardingStatus.Completed)
            return Result.Success();

        if (Status != TenantOnboardingStatus.Provisioning || CurrentStep != TenantProvisioningStep.Completed)
            return Result.Failure(InvalidTransition());

        RegistrationCompletedAtUtc = nowUtc;
        Status = TenantOnboardingStatus.Completed;
        return Result.Success();
    }

    public Result ConsumeRegistrationToken(DateTime nowUtc)
    {
        if (RegistrationTokenUsedAtUtc is not null)
            return Result.Success();

        if (RegistrationTokenHash is null)
            return Result.Failure(new Error("Onboarding.NoToken", "No registration token has been issued yet."));

        RegistrationTokenUsedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (
            Status
            is not (
                TenantOnboardingStatus.PendingPayment
                or TenantOnboardingStatus.PaymentProcessing
                or TenantOnboardingStatus.PaymentFailed
            )
        )
            return Result.Failure(InvalidTransition());

        FailureReason = reason;
        Status = TenantOnboardingStatus.Cancelled;
        return Result.Success();
    }

    public Result MarkExpired()
    {
        if (
            Status
            is not (
                TenantOnboardingStatus.PendingPayment
                or TenantOnboardingStatus.PaymentProcessing
                or TenantOnboardingStatus.RegistrationPending
            )
        )
            return Result.Failure(InvalidTransition());

        Status = TenantOnboardingStatus.Expired;
        return Result.Success();
    }

    public Result MarkRefunded(string reason)
    {
        if (Status is not (TenantOnboardingStatus.ProvisioningFailed or TenantOnboardingStatus.ManualReview))
            return Result.Failure(InvalidTransition());

        FailureReason = reason;
        Status = TenantOnboardingStatus.Refunded;
        return Result.Success();
    }

    private bool IsProvisioningAtStep(TenantProvisioningStep step) =>
        Status == TenantOnboardingStatus.Provisioning && CurrentStep == step;

    private static TenantProvisioningStep NextStep(TenantProvisioningStep step) =>
        step switch
        {
            TenantProvisioningStep.Tenant => TenantProvisioningStep.TenantAdmin,
            TenantProvisioningStep.TenantAdmin => TenantProvisioningStep.Subscription,
            TenantProvisioningStep.Subscription => TenantProvisioningStep.CloudStorage,
            TenantProvisioningStep.CloudStorage => TenantProvisioningStep.Subdomain,
            TenantProvisioningStep.Subdomain => TenantProvisioningStep.Defaults,
            TenantProvisioningStep.Defaults => TenantProvisioningStep.Completed,
            _ => step,
        };

    private static Error InvalidTransition() =>
        new("Onboarding.InvalidState", "The onboarding is not in a state that allows this operation.");
}
