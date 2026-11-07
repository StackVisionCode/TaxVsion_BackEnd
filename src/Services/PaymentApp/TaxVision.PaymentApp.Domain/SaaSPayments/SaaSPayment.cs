using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>
/// Un intento de cobro de la plataforma a un tenant (renovación de suscripción, seats,
/// add-ons, upgrade de plan). Único por <see cref="IdempotencyKey"/> (unique index en
/// Infrastructure) — reintentar con la misma key nunca produce un segundo cobro.
///
/// Máquina de estados: <c>Pending → Processing → RequiresAction → Succeeded / Failed / Cancelled</c>.
/// Post-succeeded: <c>Refunded / PartiallyRefunded / ChargedBack</c>. Cada transición es un
/// método explícito con su propia precondición (guardrail §48.2) — no existe un
/// ChangeStatus(...) genérico. El aggregate nunca importa un SDK de provider (guardrail
/// §39.9): solo conoce <see cref="PaymentProviderCode"/> y VOs de intercambio.
/// </summary>
public sealed class SaaSPayment : AggregateRoot
{
    private readonly List<SaaSPaymentAttempt> _attempts = [];
    private readonly List<RefundLine> _refunds = [];
    private readonly List<RefundRequest> _refundRequests = [];

    public IdempotencyKey IdempotencyKey { get; private set; } = null!;
    public Money Amount { get; private set; } = null!;

    /// <summary>Desglose del importe para el recibo. Ausente cuando el cobro no tiene unidades que contar.</summary>
    public ChargeBreakdown? Breakdown { get; private set; }
    public SaaSPaymentType Type { get; private set; }
    public Guid TargetAggregateId { get; private set; }
    public PaymentProviderCode ProviderCode { get; private set; }
    public PaymentStatus Status { get; private set; }
    public ExternalPaymentReference? ExternalChargeReference { get; private set; }
    public StatementDescriptor StatementDescriptor { get; private set; } = null!;
    public string? NextActionType { get; private set; }
    public string? NextActionUrl { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public DateTime? ChargedBackAtUtc { get; private set; }
    public bool IsLegalHeld { get; private set; }

    /// <summary>Set only when this charge carries a Growth/Codes discount reservation (e.g. a
    /// referral welcome benefit) that must be committed or cancelled once the charge reaches a
    /// terminal state. <see cref="CodeReservationPaymentId"/> is the opaque PaymentId the
    /// reservation was bound to at Codes.CreateSystemQuote/ReserveCode time — it does not have
    /// to equal <see cref="BaseEntity.Id"/>, it only has to match exactly when committing.
    /// <see cref="Amount"/> is already the net (discounted) amount charged to the provider.</summary>
    public Guid? CodeReservationId { get; private set; }
    public Guid? CodeReservationPaymentId { get; private set; }
    public long? DiscountAmountCents { get; private set; }
    public string? PromotionSnapshotHash { get; private set; }

    /// <summary>PayFlow (Fase 8) — solo poblado cuando <see cref="Type"/> es
    /// <see cref="SaaSPaymentType.OnboardingInitial"/>: el <c>TenantOnboarding</c> que este pago
    /// financia, del lado de Auth. No hay FK — Auth y PaymentApp son bases de datos separadas.</summary>
    public Guid? OnboardingId { get; private set; }

    /// <summary>El PDF del recibo, una vez que Documents lo generó y CloudStorage lo guardó.</summary>
    public Guid? ReceiptFileId { get; private set; }

    /// <summary>PayFlow (Fase 8) — id de la Checkout Session hosteada del provider (p.ej.
    /// <c>cs_...</c> de Stripe). Distinto de <see cref="ExternalChargeReference"/>, que guarda
    /// la referencia del PaymentIntent subyacente (lo que el webhook usa para resolver el pago).</summary>
    public string? ProviderCheckoutSessionId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid CreatedBy { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    // Opcional. Null para pagos legacy; el consumer de recibos cae al mapping por PaymentType cuando falta.
    public ChargeConcept? Concept { get; private set; }

    // Revisión manual: éxito del proveedor que no se pudo aplicar. No cambia Status.
    public DateTime? ReviewRequiredAtUtc { get; private set; }
    public string? ReviewReasonCode { get; private set; }
    public string? ReviewProviderReference { get; private set; }
    public long? ReviewPaidAmountCents { get; private set; }
    public string? ReviewPaidCurrency { get; private set; }
    public DateTime? ReviewResolvedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "Admin de plataforma que resolvió la revisión: historia.")]
    public Guid? ReviewResolvedByUserId { get; private set; }
    public string? ReviewResolution { get; private set; }

    /// <summary>Cuándo se llamó por primera vez al provider en este intento. Con valor y sin resultado, el cargo pudo
    /// existir: no se reenvía a ciegas (BB-8). Lo limpia <see cref="PrepareForRetry"/>.</summary>
    public DateTime? ChargeAttemptedAtUtc { get; private set; }

    public IReadOnlyCollection<SaaSPaymentAttempt> Attempts => _attempts;
    public IReadOnlyCollection<RefundLine> Refunds => _refunds;
    public IReadOnlyCollection<RefundRequest> RefundRequests => _refundRequests;

    private SaaSPayment() { }

    /// <summary>Etiqueta del recibo. Se fija una sola vez, antes de cobrar.</summary>
    public Result AttachConcept(ChargeConcept concept)
    {
        if (Status != PaymentStatus.Pending || Concept is not null)
            return Result.Failure(
                new Error("SaaSPayment.ConceptAlreadySet", "The concept can only be set once, before the charge.")
            );

        Concept = concept;
        return Result.Success();
    }

    public static Result<SaaSPayment> Create(
        Guid tenantId,
        IdempotencyKey key,
        Money amount,
        SaaSPaymentType type,
        Guid targetAggregateId,
        PaymentProviderCode provider,
        StatementDescriptor descriptor,
        Guid actorUserId,
        DateTime nowUtc,
        Guid? codeReservationId = null,
        Guid? codeReservationPaymentId = null,
        long? discountAmountCents = null,
        string? promotionSnapshotHash = null,
        ChargeBreakdown? breakdown = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SaaSPayment>(new Error("SaaSPayment.InvalidTenant", "TenantId is required."));

        if (targetAggregateId == Guid.Empty)
            return Result.Failure<SaaSPayment>(
                new Error("SaaSPayment.InvalidTarget", "TargetAggregateId is required.")
            );

        if (amount.AmountCents <= 0)
            return Result.Failure<SaaSPayment>(
                new Error("SaaSPayment.InvalidAmount", "Amount must be greater than zero.")
            );

        // O el desglose cuadra con lo cobrado, o no hay desglose: un recibo cuyas cuentas no salen
        // es peor que uno que solo muestra el total.
        if (breakdown is not null && breakdown.TotalCents != amount.AmountCents)
            return Result.Failure<SaaSPayment>(
                new Error("SaaSPayment.BreakdownMismatch", "The breakdown does not add up to the amount charged.")
            );

        var payment = new SaaSPayment
        {
            IdempotencyKey = key,
            Amount = amount,
            Breakdown = breakdown,
            Type = type,
            TargetAggregateId = targetAggregateId,
            ProviderCode = provider,
            Status = PaymentStatus.Pending,
            StatementDescriptor = descriptor,
            CodeReservationId = codeReservationId,
            CodeReservationPaymentId = codeReservationPaymentId,
            DiscountAmountCents = discountAmountCents,
            PromotionSnapshotHash = promotionSnapshotHash,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        payment.SetTenant(tenantId);
        return Result.Success(payment);
    }

    /// <summary>
    /// PayFlow (Fase 8) — primer pago de un onboarding pago-primero, antes de que el tenant
    /// exista. A diferencia de <see cref="Create"/>, permite deliberadamente
    /// <c>TenantId=Guid.Empty</c> como sentinel: no hay FK de <c>SaaSPayments.TenantId</c> hacia
    /// una tabla Tenants (confirmado en <c>SaaSPaymentConfiguration</c>), y los repos de lectura
    /// que necesitan encontrar este pago antes de conocer el tenant (por IdempotencyKey, por
    /// ExternalChargeReference) ya usan <c>IgnoreQueryFilters()</c> explícito — el filtro global
    /// fail-closed de RBAC Fase 5 nunca los alcanza. El tenant real se reconcilia más adelante
    /// (Fase 15, cuando el Saga de Auth crea el tenant) mediante un mecanismo fuera del alcance
    /// de esta fase.
    /// </summary>
    public static Result<SaaSPayment> CreateForOnboarding(
        Guid onboardingId,
        IdempotencyKey key,
        Money amount,
        Guid planId,
        PaymentProviderCode provider,
        StatementDescriptor descriptor,
        DateTime nowUtc
    )
    {
        if (onboardingId == Guid.Empty)
            return Result.Failure<SaaSPayment>(new Error("SaaSPayment.InvalidOnboarding", "OnboardingId is required."));

        if (planId == Guid.Empty)
            return Result.Failure<SaaSPayment>(new Error("SaaSPayment.InvalidTarget", "PlanId is required."));

        if (amount.AmountCents <= 0)
            return Result.Failure<SaaSPayment>(
                new Error("SaaSPayment.InvalidAmount", "Amount must be greater than zero.")
            );

        var payment = new SaaSPayment
        {
            IdempotencyKey = key,
            Amount = amount,
            Type = SaaSPaymentType.OnboardingInitial,
            TargetAggregateId = planId,
            ProviderCode = provider,
            Status = PaymentStatus.Pending,
            StatementDescriptor = descriptor,
            OnboardingId = onboardingId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = Guid.Empty,
            UpdatedBy = Guid.Empty,
        };
        payment.SetTenant(Guid.Empty);
        return Result.Success(payment);
    }

    /// <summary>PayFlow (Fase 8) — vincula la Checkout Session hosteada del provider a este
    /// pago y lo marca en curso. Desde la perspectiva del dominio, entregarle el flujo al
    /// checkout hosteado equivale a "sometimos el intento de cobro y esperamos su resultado" —
    /// lo mismo que <see cref="MarkProcessing"/> para un cargo directo, solo que la referencia
    /// externa que correlaciona el webhook posterior (<paramref name="paymentIntentReference"/>)
    /// llega junto con el id de sesión en vez de con el resultado inmediato de un charge.</summary>
    /// <summary>
    /// Re-hospeda el pago de un onboarding al tenant real, una vez que la saga lo crea. Nace con
    /// <c>TenantId = Guid.Empty</c> porque el tenant todavía no existe, y hasta que esto corre es invisible
    /// para su dueño. Idempotente. Molde: <c>Billing.Invoice.RehomeToTenant</c>.
    /// </summary>
    /// <summary>Cuelga el recibo ya generado. Idempotente: una redelivery no lo cambia.</summary>
    public Result AttachReceipt(Guid receiptFileId, DateTime nowUtc)
    {
        if (receiptFileId == Guid.Empty)
            return Result.Failure(new Error("SaaSPayment.InvalidReceipt", "A receipt file id is required."));

        if (ReceiptFileId == receiptFileId)
            return Result.Success();

        ReceiptFileId = receiptFileId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result RehomeToTenant(Guid realTenantId, DateTime nowUtc)
    {
        if (realTenantId == Guid.Empty)
            return Result.Failure(new Error("SaaSPayment.InvalidTenant", "Real tenant is required."));

        if (OnboardingId is null)
            return Result.Failure(
                new Error("SaaSPayment.NotOnboarding", "Only an onboarding payment can be re-homed to a tenant.")
            );

        if (TenantId == realTenantId)
            return Result.Success();

        SetTenant(realTenantId);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result RecordHostedCheckoutSession(
        string providerSessionId,
        ExternalPaymentReference paymentIntentReference,
        string checkoutUrl,
        DateTime nowUtc
    )
    {
        if (Status == PaymentStatus.Processing && ProviderCheckoutSessionId == providerSessionId)
            return Result.Success();

        if (Status != PaymentStatus.Pending)
            return Result.Failure(
                new Error("SaaSPayment.InvalidTransition", $"Cannot record a checkout session from {Status}.")
            );

        if (string.IsNullOrWhiteSpace(providerSessionId))
            return Result.Failure(new Error("SaaSPayment.InvalidCheckoutSession", "ProviderSessionId is required."));

        ProviderCheckoutSessionId = providerSessionId;
        ExternalChargeReference = paymentIntentReference;
        NextActionType = "HostedCheckout";
        NextActionUrl = checkoutUrl;
        Status = PaymentStatus.Processing;
        _attempts.Add(
            SaaSPaymentAttempt.Record(
                Id,
                TenantId,
                _attempts.Count + 1,
                providerResponseCode: "checkout_session_created",
                providerResponseBody: null,
                nowUtc
            )
        );
        Touch(Guid.Empty, nowUtc);
        return Result.Success();
    }

    /// <summary>Registra el envío del cobro al provider y agrega el intento correspondiente
    /// al historial auditable.</summary>
    public Result MarkProcessing(
        ExternalPaymentReference reference,
        string? providerResponseCode,
        string? providerResponseBody,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot mark processing from {Status}."));

        ExternalChargeReference = reference;
        Status = PaymentStatus.Processing;
        _attempts.Add(
            SaaSPaymentAttempt.Record(
                Id,
                TenantId,
                _attempts.Count + 1,
                providerResponseCode,
                providerResponseBody,
                nowUtc
            )
        );
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>El provider requiere una acción adicional del pagador (3DS / SCA) antes de
    /// poder confirmar el cobro.</summary>
    public Result MarkRequiresAction(string nextActionType, string nextActionUrl, Guid actorUserId, DateTime nowUtc)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot require action from {Status}."));

        if (string.IsNullOrWhiteSpace(nextActionType))
            return Result.Failure(new Error("SaaSPayment.InvalidNextAction", "NextActionType is required."));

        Status = PaymentStatus.RequiresAction;
        NextActionType = nextActionType;
        NextActionUrl = nextActionUrl;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>PayFlow — Stripe documenta <c>payment_intent</c> como <c>nullable</c> en la
    /// respuesta de creación de una Checkout Session en modo <c>payment</c> (no se crea
    /// sincrónicamente pese a <c>Expand</c>). <see cref="RecordHostedCheckoutSession"/> guarda
    /// por eso el id de la propia Session como referencia provisoria; cuando el webhook
    /// <c>checkout.session.completed</c> confirma el pago y ya trae el PaymentIntent real, este
    /// método lo reconcilia para que futuros webhooks de refund/dispute (que sí referencian el
    /// PaymentIntent/charge, no la Session) puedan resolver este pago.</summary>
    public Result ReconcileProviderChargeReference(ExternalPaymentReference reference, DateTime nowUtc)
    {
        if (Status is not (PaymentStatus.Processing or PaymentStatus.RequiresAction or PaymentStatus.Succeeded))
            return Result.Failure(
                new Error("SaaSPayment.InvalidTransition", $"Cannot reconcile charge reference from {Status}.")
            );

        ExternalChargeReference = reference;
        Touch(Guid.Empty, nowUtc);
        return Result.Success();
    }

    public Result MarkSucceeded(DateTime paidAtUtc, Guid actorUserId)
    {
        if (Status is not (PaymentStatus.Processing or PaymentStatus.RequiresAction))
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot mark succeeded from {Status}."));

        Status = PaymentStatus.Succeeded;
        PaidAtUtc = paidAtUtc;
        NextActionType = null;
        NextActionUrl = null;
        Touch(actorUserId, paidAtUtc);
        AddDomainEvent(new SaaSPaymentSucceeded(Snapshot()));
        return Result.Success();
    }

    /// <summary><paramref name="retry"/> es lo que decidió <see cref="DunningPolicy"/>.</summary>
    public Result MarkFailed(
        string failureCode,
        string failureReason,
        DunningDecision retry,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (
            Status
            is PaymentStatus.Succeeded
                or PaymentStatus.Refunded
                or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Cancelled
                or PaymentStatus.ChargedBack
        )
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot mark failed from {Status}."));

        if (string.IsNullOrWhiteSpace(failureCode))
            return Result.Failure(new Error("SaaSPayment.InvalidFailureCode", "FailureCode is required."));

        // Un cobro que falla antes de llegar a Processing también es un intento: sin contarlo,
        // el backoff de dunning nunca avanzaba y el reintento no terminaba nunca.
        if (Status == PaymentStatus.Pending)
            _attempts.Add(SaaSPaymentAttempt.Record(Id, TenantId, _attempts.Count + 1, failureCode, null, nowUtc));

        Status = PaymentStatus.Failed;
        FailureCode = failureCode;
        FailureReason = failureReason;
        NextRetryAtUtc = retry.NextRetryAtUtc;
        NextActionType = null;
        NextActionUrl = null;
        Touch(actorUserId, nowUtc);
        AddDomainEvent(new SaaSPaymentFailed(Snapshot()));
        return Result.Success();
    }

    /// <summary>Traduce la respuesta de un cobro off-session a transiciones. El provider ya respondió: una transición
    /// inválida no se reporta y el pago queda como está.</summary>
    public OffSessionChargeApplied ApplyOffSessionOutcome(OffSessionChargeResponse response, DateTime nowUtc)
    {
        // El provider devuelve una referencia útil solo cuando pudo crear el intent: no se exige para los fallos
        // sin referencia (red caída, auth fallida antes de hablar con el banco).
        if (response.Reference is { } reference)
            MarkProcessing(reference, response.ResponseCode, providerResponseBody: null, Guid.Empty, nowUtc);

        switch (response.Result)
        {
            case OffSessionChargeResult.Succeeded:
                // Sin referencia no se puede marcar Processing, y el evento de éxito la lleva: queda Failed y el
                // flow manual lo retoma.
                if (Status == PaymentStatus.Pending)
                    return FailOffSession(
                        "OffSession.MissingReference",
                        "Provider returned Succeeded with no reference.",
                        DunningPolicy.Decide(this, nowUtc),
                        nowUtc
                    );
                MarkSucceeded(nowUtc, Guid.Empty);
                return new OffSessionChargeApplied(ProviderConfirmed: true);

            case OffSessionChargeResult.RequiresUserAction:
                // SCA/3DS: no cierra el intent; el banner "Pay now" arranca el on-session cuando el usuario vuelve.
                var actionCode = response.FailureCode ?? "authentication_required";
                if (Status == PaymentStatus.Pending)
                    return FailOffSession(
                        actionCode,
                        response.FailureMessage ?? "Issuing bank requires customer authentication.",
                        DunningPolicy.Decide(this, nowUtc),
                        nowUtc
                    ) with
                    {
                        NeedsUserAction = true,
                    };
                MarkRequiresAction(actionCode, nextActionUrl: string.Empty, actorUserId: Guid.Empty, nowUtc);
                return new OffSessionChargeApplied(NeedsUserAction: true);

            case OffSessionChargeResult.NoFinalOutcome:
                // Con referencia lo consulta el job de Processing; sin ella sigue Pending y lo busca la reconciliación.
                return new OffSessionChargeApplied(NoFinalOutcome: true);

            default:
                return FailOffSession(
                    response.FailureCode ?? "Provider.Failed",
                    response.FailureMessage ?? "Off-session charge was declined.",
                    // Con referencia, el intento ya quedó registrado en MarkProcessing: no contarlo dos veces.
                    DunningPolicy.Decide(this, nowUtc, failedAttemptRecorded: Status == PaymentStatus.Processing),
                    nowUtc
                );
        }
    }

    private OffSessionChargeApplied FailOffSession(string code, string message, DunningDecision retry, DateTime nowUtc)
    {
        MarkFailed(code, message, DunningPolicy.ForFailure(code, retry), Guid.Empty, nowUtc);
        return new OffSessionChargeApplied(FailureCode: code);
    }

    /// <summary>Graba el intento antes de llamar al provider. Un reintento conserva la primera fecha: de ahí corre el
    /// plazo en que el provider recuerda la clave.</summary>
    public Result MarkChargeAttempted(DateTime nowUtc)
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(
                new Error("SaaSPayment.InvalidTransition", $"Cannot start a charge attempt from {Status}.")
            );

        ChargeAttemptedAtUtc ??= nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Clave que se manda al provider en el próximo intento. El primero usa la clave del pago; los
    /// reintentos le suman el número: Stripe rechaza una clave reusada con otros datos y, pasadas 24 h,
    /// la olvida y crea un cobro nuevo.
    /// </summary>
    public IdempotencyKey ProviderIdempotencyKeyForNextAttempt()
    {
        var attemptNumber = _attempts.Count + 1;
        if (attemptNumber == 1)
            return IdempotencyKey;

        var perAttempt = ValueObjects.IdempotencyKey.Create($"{IdempotencyKey.Value}-attempt-{attemptNumber}");
        return perAttempt.IsSuccess ? perAttempt.Value : IdempotencyKey;
    }

    /// <summary>Reabre un pago Failed para un nuevo intento — lo usa
    /// <c>RetrySaaSPaymentHandler</c> (dunning), nunca un caller directo. Solo procede si el
    /// intento anterior dejó agendado un retry (<see cref="NextRetryAtUtc"/> no nulo) y ya
    /// llegó su hora.</summary>
    public Result PrepareForRetry(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != PaymentStatus.Failed)
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot retry from {Status}."));

        if (NextRetryAtUtc is null)
            return Result.Failure(new Error("SaaSPayment.NoRetryScheduled", "This payment has no retry scheduled."));

        if (nowUtc < NextRetryAtUtc)
            return Result.Failure(
                new Error("SaaSPayment.RetryNotDue", "The scheduled retry time has not arrived yet.")
            );

        Status = PaymentStatus.Pending;
        FailureCode = null;
        FailureReason = null;
        NextRetryAtUtc = null;
        ChargeAttemptedAtUtc = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>PayFlow — reabre un pago de onboarding Failed para un nuevo intento de checkout
    /// iniciado por el comprador. A diferencia de <see cref="PrepareForRetry"/> (dunning, que exige un
    /// retry agendado en <see cref="NextRetryAtUtc"/>), esto no depende de un agendado: lo dispara el
    /// reintento manual. Limpia la sesión y la referencia externa del intento anterior para que se
    /// registre una nueva — así un webhook tardío del intento viejo ya no resuelve a este pago
    /// (<c>GetByExternalReferenceAsync</c> no lo encuentra) y no puede corromper el intento nuevo.</summary>
    public Result PrepareForOnboardingRetry(DateTime nowUtc)
    {
        if (Type != SaaSPaymentType.OnboardingInitial)
            return Result.Failure(
                new Error("SaaSPayment.InvalidTransition", "Only onboarding payments can be reopened this way.")
            );

        // Cancelled cuenta como fallo para onboarding (la sesión expiró y el proveedor anuló el intento):
        // el comprador no pagó, así que puede reintentar igual que si lo hubieran declinado.
        if (Status is not (PaymentStatus.Failed or PaymentStatus.Cancelled))
            return Result.Failure(
                new Error("SaaSPayment.InvalidTransition", $"Cannot retry onboarding checkout from {Status}.")
            );

        Status = PaymentStatus.Pending;
        FailureCode = null;
        FailureReason = null;
        NextRetryAtUtc = null;
        ProviderCheckoutSessionId = null;
        ExternalChargeReference = null;
        NextActionType = null;
        NextActionUrl = null;
        Touch(Guid.Empty, nowUtc);
        return Result.Success();
    }

    /// <summary>Cancelación propagada desde Subscription: el comprador cerró el checkout voluntariamente
    /// (o el sweeper venció un intent que nunca se pagó). A diferencia de <see cref="CancelByAdmin"/>, no
    /// la dispara un admin — refleja el estado del intent dueño del cobro — así que es idempotente ante
    /// redeliveries y gana a un posterior <c>checkout.session.expired</c> del provider: el adapter traduce
    /// esa expiración a <see cref="PaymentStatus.Failed"/>, pero una vez acá en Cancelled el webhook se
    /// descarta como stale y el historial muestra "Cancelled" en lugar de "Failed".</summary>
    public Result MarkCancelled(string reason, DateTime cancelledAtUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("SaaSPayment.InvalidReason", "Reason is required."));

        // Idempotente ante redeliveries del event bus: ya está en Cancelled, no hay nada que hacer.
        if (Status == PaymentStatus.Cancelled)
            return Result.Success();

        // Terminales no-Cancelled (Succeeded / Refunded / PartiallyRefunded / ChargedBack) ganan: un pago
        // que ya cobró no se re-escribe como Cancelled porque llegó tarde un event de cancelación.
        if (
            Status
            is PaymentStatus.Succeeded
                or PaymentStatus.Refunded
                or PaymentStatus.PartiallyRefunded
                or PaymentStatus.ChargedBack
        )
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot mark cancelled from {Status}."));

        Status = PaymentStatus.Cancelled;
        FailureReason = reason;
        NextActionType = null;
        NextActionUrl = null;
        NextRetryAtUtc = null;
        Touch(Guid.Empty, cancelledAtUtc);
        AddDomainEvent(new SaaSPaymentCancelled(Snapshot(), PropagatedFromOwner: true));
        return Result.Success();
    }

    public Result CancelByAdmin(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (IsLegalHeld)
            return Result.Failure(
                new Error("Payment.LegalHeld", "Payment is under legal hold and cannot be cancelled.")
            );

        if (
            Status
            is not (
                PaymentStatus.Pending
                or PaymentStatus.Processing
                or PaymentStatus.RequiresAction
                or PaymentStatus.Failed
            )
        )
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot cancel from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("SaaSPayment.InvalidReason", "Reason is required."));

        Status = PaymentStatus.Cancelled;
        FailureReason = reason;
        Touch(actorUserId, nowUtc);
        AddDomainEvent(new SaaSPaymentCancelled(Snapshot(), PropagatedFromOwner: false));
        return Result.Success();
    }

    /// <summary>El provider confirmó el cobro. Si reporta monto, tiene que coincidir con el esperado (y la moneda, si
    /// viene): así el pago no queda Succeeded por otro importe (captura parcial, mala configuración). Sin monto no hay
    /// nada que verificar y se aplica igual.</summary>
    public Result ApplyProviderSuccess(long? paidAmountCents, string? paidCurrency, DateTime nowUtc)
    {
        if (paidAmountCents is { } paidCents)
        {
            var currencyMatches =
                paidCurrency is null
                || string.Equals(paidCurrency, Amount.Currency, StringComparison.OrdinalIgnoreCase);
            if (paidCents != Amount.AmountCents || !currencyMatches)
                return Result.Failure(
                    new Error(
                        AmountMismatchCode,
                        $"Provider reported {paidCents} {paidCurrency ?? "?"} paid but the charge expects "
                            + $"{Amount.AmountCents} {Amount.Currency}; not applying."
                    )
                );
        }

        return MarkSucceeded(nowUtc, Guid.Empty);
    }

    /// <summary>El provider avisó un fallo tardío: sigue el mismo dunning que el cobro síncrono. Si ya estaba Failed,
    /// reaplicarlo reprogramaría el dunning y publicaría el resultado dos veces.</summary>
    public Result ApplyProviderFailure(string? failureCode, string? failureMessage, DateTime nowUtc)
    {
        if (Status == PaymentStatus.Failed)
            return Result.Failure(new Error("SaaSPayment.AlreadyFailed", "The payment is already marked as failed."));

        return MarkFailed(
            failureCode ?? "Provider.Unknown",
            failureMessage ?? "The provider declined the charge.",
            DunningPolicy.Decide(this, nowUtc, failedAttemptRecorded: true),
            Guid.Empty,
            nowUtc
        );
    }

    /// <summary>El provider reporta el total reembolsado: se aplica solo lo que falta registrar. Devuelve true si
    /// agregó un reembolso.</summary>
    public Result<bool> ApplyProviderRefundTotal(long totalRefundedCents, DateTime nowUtc)
    {
        var deltaCents = totalRefundedCents - SumOfRefunds();
        if (deltaCents <= 0)
            return Result.Success(false);

        var delta = Money.Create(deltaCents, Amount.Currency);
        if (delta.IsFailure)
            return Result.Failure<bool>(delta.Error);

        var refunded = RefundPartial(delta.Value, "Refunded via provider webhook.", Guid.Empty, nowUtc);
        return refunded.IsFailure ? Result.Failure<bool>(refunded.Error) : Result.Success(true);
    }

    /// <summary>Total ya reembolsado, en centavos.</summary>
    public long RefundedAmountCents => SumOfRefunds();

    /// <summary>Valida un reembolso sin mutar nada; se llama antes de pedirle el dinero al proveedor.</summary>
    public Result CanRefund(Money refundAmount, string reason)
    {
        if (IsLegalHeld)
            return Result.Failure(
                new Error("Payment.LegalHeld", "Payment is under legal hold and cannot be refunded.")
            );

        if (Status is not (PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded))
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot refund from {Status}."));

        if (refundAmount.Currency != Amount.Currency)
            return Result.Failure(
                new Error("SaaSPayment.CurrencyMismatch", "Refund currency must match the original payment currency.")
            );

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("SaaSPayment.InvalidReason", "Reason is required."));

        if (SumOfRefunds() + refundAmount.AmountCents > Amount.AmountCents)
            return Result.Failure(
                new Error("SaaSPayment.RefundExceedsPrincipal", "Refund amount exceeds the original payment amount.")
            );

        return Result.Success();
    }

    /// <summary>Saldo que un reembolso total devolvería, validado con las mismas reglas de <see cref="CanRefund"/>.</summary>
    public Result<Money> CanRefundFull(string reason)
    {
        var remaining = Amount.Subtract(Money.Create(SumOfRefunds(), Amount.Currency).Value);
        if (remaining.IsFailure)
            return remaining;

        var check = CanRefund(remaining.Value, reason);
        return check.IsFailure ? Result.Failure<Money>(check.Error) : remaining;
    }

    public Result RefundPartial(Money refundAmount, string reason, Guid actorUserId, DateTime nowUtc)
    {
        var check = CanRefund(refundAmount, reason);
        if (check.IsFailure)
            return check;

        var totalRefunded = SumOfRefunds();
        _refunds.Add(RefundLine.Create(Id, TenantId, refundAmount, reason, actorUserId, nowUtc));

        Status =
            totalRefunded + refundAmount.AmountCents == Amount.AmountCents
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RefundFull(string reason, Guid actorUserId, DateTime nowUtc)
    {
        var remaining = CanRefundFull(reason);
        if (remaining.IsFailure)
            return Result.Failure(remaining.Error);

        return RefundPartial(remaining.Value, reason, actorUserId, nowUtc);
    }

    /// <summary>La última solicitud de reembolso con esa clave de petición, o null.</summary>
    public RefundRequest? FindRefundRequest(string requestKey) =>
        _refundRequests
            .Where(request => request.RequestKey == requestKey)
            .OrderByDescending(request => request.CreatedAtUtc)
            .FirstOrDefault();

    /// <summary>Guarda la intención antes de llamar al proveedor (BB-8). Mismas reglas que <see cref="CanRefund"/> y
    /// una sola solicitud sin resultado a la vez: otra en curso podría pasar el tope sin que nadie lo vea.</summary>
    public Result<RefundRequest> OpenRefundRequest(
        Money refundAmount,
        string reason,
        Guid actorUserId,
        string requestKey,
        string? providerIdempotencyKey,
        DateTime nowUtc
    )
    {
        var check = CanRefund(refundAmount, reason);
        if (check.IsFailure)
            return Result.Failure<RefundRequest>(check.Error);

        if (string.IsNullOrWhiteSpace(requestKey))
            return Result.Failure<RefundRequest>(
                new Error("SaaSPayment.InvalidRefundRequest", "A refund request key is required.")
            );

        if (_refundRequests.Any(request => request.Status == RefundRequestStatus.Pending))
            return Result.Failure<RefundRequest>(
                new Error(
                    "SaaSPayment.RefundInProgress",
                    "Another refund of this payment has no result yet; retry that request first."
                )
            );

        var request = RefundRequest.Open(
            Id,
            TenantId,
            requestKey,
            providerIdempotencyKey,
            refundAmount,
            reason,
            actorUserId,
            SumOfRefunds(),
            nowUtc
        );
        _refundRequests.Add(request);
        Touch(actorUserId, nowUtc);
        return Result.Success(request);
    }

    /// <summary>El proveedor reembolsó: se registra una vez. Si la sincronización del proveedor ya lo registró, solo
    /// cierra la solicitud. Devuelve false si no agregó línea.</summary>
    public Result<bool> CompleteRefundRequest(Guid requestId, string? providerRefundReference, DateTime nowUtc)
    {
        var request = PendingRefundRequest(requestId);
        if (request.IsFailure)
            return Result.Failure<bool>(request.Error);

        var pending = request.Value;
        if (SumOfRefunds() >= pending.RefundedCentsBefore + pending.Amount.AmountCents)
        {
            pending.Resolve(RefundRequestStatus.Completed, providerRefundReference, null, nowUtc);
            Touch(pending.RequestedByUserId, nowUtc);
            return Result.Success(false);
        }

        var refunded = RefundPartial(pending.Amount, pending.Reason, pending.RequestedByUserId, nowUtc);
        if (refunded.IsFailure)
            return Result.Failure<bool>(refunded.Error);

        if (!string.IsNullOrWhiteSpace(providerRefundReference))
            _refunds[^1].AttachExternalReference(providerRefundReference);
        pending.Resolve(RefundRequestStatus.Completed, providerRefundReference, null, nowUtc);
        return Result.Success(true);
    }

    /// <summary>El proveedor rechazó el reembolso: no se movió dinero.</summary>
    public Result RejectRefundRequest(Guid requestId, string failureCode, DateTime nowUtc)
    {
        var request = PendingRefundRequest(requestId);
        if (request.IsFailure)
            return request;

        request.Value.Resolve(RefundRequestStatus.Rejected, null, failureCode, nowUtc);
        Touch(request.Value.RequestedByUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>El proveedor reembolsó (o pudo hacerlo) y no se puede registrar: la solicitud no se vuelve a enviar y el
    /// pago queda para revisión. Devuelve true si el pago quedó marcado ahora (false si ya tenía otra marca).</summary>
    public Result<bool> MarkRefundNeedsReconciliation(
        Guid requestId,
        string? providerRefundReference,
        string failureCode,
        DateTime nowUtc
    )
    {
        var request = PendingRefundRequest(requestId);
        if (request.IsFailure)
            return Result.Failure<bool>(request.Error);

        var pending = request.Value;
        pending.Resolve(RefundRequestStatus.NeedsReconciliation, providerRefundReference, failureCode, nowUtc);
        Touch(pending.RequestedByUserId, nowUtc);
        return FlagRefundForReview(
            ReviewReasonRefundNeedsReconciliation,
            providerRefundReference,
            pending.Amount,
            nowUtc
        );
    }

    /// <summary>Marca el pago para revisión por un reembolso. False si ya tenía una marca (no se pisa).</summary>
    public Result<bool> FlagRefundForReview(string reasonCode, string? providerReference, Money amount, DateTime nowUtc)
    {
        if (ReviewRequiredAtUtc is not null)
            return Result.Success(false);

        var flagged = FlagForReview(reasonCode, providerReference, amount.AmountCents, amount.Currency, nowUtc);
        return flagged.IsFailure ? Result.Failure<bool>(flagged.Error) : Result.Success(true);
    }

    private Result<RefundRequest> PendingRefundRequest(Guid requestId)
    {
        var request = _refundRequests.FirstOrDefault(candidate => candidate.Id == requestId);
        if (request is null)
            return Result.Failure<RefundRequest>(
                new Error("SaaSPayment.RefundRequestNotFound", "The refund request does not exist.")
            );

        return request.Status == RefundRequestStatus.Pending
            ? Result.Success(request)
            : Result.Failure<RefundRequest>(
                new Error("SaaSPayment.RefundRequestResolved", $"The refund request is already {request.Status}.")
            );
    }

    public Result MarkChargedBack(DateTime chargedBackAtUtc, string reason, Guid actorUserId)
    {
        if (Status is not (PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded))
            return Result.Failure(
                new Error("SaaSPayment.InvalidTransition", $"Cannot mark charged back from {Status}.")
            );

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("SaaSPayment.InvalidReason", "Reason is required."));

        Status = PaymentStatus.ChargedBack;
        ChargedBackAtUtc = chargedBackAtUtc;
        FailureReason = reason;
        Touch(actorUserId, chargedBackAtUtc);
        return Result.Success();
    }

    /// <summary>Coloca o levanta el legal hold. Es una acción humana: exige actor y, si el valor no cambia, no hace nada.
    /// Todavía no hay handler ni endpoint que la llame.</summary>
    public Result SetLegalHold(bool isLegalHeld, Guid actorUserId, DateTime nowUtc)
    {
        if (actorUserId == Guid.Empty)
            return Result.Failure(
                new Error("SaaSPayment.ActorRequired", "An actor is required to change a legal hold.")
            );

        if (IsLegalHeld == isLegalHeld)
            return Result.Success();

        IsLegalHeld = isLegalHeld;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>El monto o la moneda que confirmó el provider no son los del cobro.</summary>
    public const string AmountMismatchCode = "WebhookEvent.AmountMismatch";

    public const string ReviewReasonLateProviderSuccess = "LateProviderSuccess";
    public const string ReviewReasonAmountMismatch = "AmountMismatch";

    /// <summary>Se llamó al provider, no quedó resultado y el provider no permite buscar el cargo.</summary>
    public const string ReviewReasonChargeOutcomeUnknown = "ChargeOutcomeUnknown";

    /// <summary>El proveedor rechazó el reembolso de onboarding: el dinero sigue cobrado.</summary>
    public const string ReviewReasonRefundRejected = "RefundRejected";

    /// <summary>El proveedor reembolsó (o pudo hacerlo) y no quedó registrado: se concilia, no se reembolsa otra vez.</summary>
    public const string ReviewReasonRefundNeedsReconciliation = "RefundNeedsReconciliation";

    private static readonly HashSet<string> ReviewReasons =
    [
        ReviewReasonLateProviderSuccess,
        ReviewReasonAmountMismatch,
        ReviewReasonChargeOutcomeUnknown,
        ReviewReasonRefundRejected,
        ReviewReasonRefundNeedsReconciliation,
    ];
    private static readonly HashSet<string> ReviewResolutions = ["Refunded", "BenefitApplied", "NoActionNeeded"];

    /// <summary>Marca el pago para revisión manual: el proveedor confirmó un cobro que no se pudo aplicar.
    /// No toca <see cref="Status"/> y es idempotente: si ya está marcado, conserva la primera marca.</summary>
    public Result FlagForReview(
        string reasonCode,
        string? providerReference,
        long? paidAmountCents,
        string? paidCurrency,
        DateTime nowUtc
    )
    {
        if (!ReviewReasons.Contains(reasonCode))
            return Result.Failure(
                new Error("SaaSPayment.InvalidReviewReason", $"Unknown review reason '{reasonCode}'.")
            );

        if (ReviewRequiredAtUtc is not null)
            return Result.Success();

        var currency = paidCurrency?.Trim().ToUpperInvariant();
        ReviewRequiredAtUtc = nowUtc;
        ReviewReasonCode = reasonCode;
        ReviewProviderReference = string.IsNullOrWhiteSpace(providerReference) ? null : providerReference.Trim();
        ReviewPaidAmountCents = paidAmountCents;
        ReviewPaidCurrency = currency is { Length: 3 } ? currency : null;
        AddDomainEvent(
            new SaaSPaymentReviewRequired(
                Id,
                TenantId,
                reasonCode,
                ReviewProviderReference,
                paidAmountCents,
                ReviewPaidCurrency,
                nowUtc
            )
        );
        return Result.Success();
    }

    /// <summary>Registra la decisión que el admin tomó a mano (reembolso, beneficio o nada). No mueve dinero.</summary>
    public Result ResolveReview(string resolution, Guid actorUserId, DateTime nowUtc)
    {
        if (ReviewRequiredAtUtc is null)
            return Result.Failure(
                new Error("SaaSPayment.ReviewNotRequired", "This payment is not flagged for review.")
            );

        if (ReviewResolvedAtUtc is not null)
            return Result.Failure(new Error("SaaSPayment.ReviewAlreadyResolved", "This review was already resolved."));

        if (!ReviewResolutions.Contains(resolution))
            return Result.Failure(
                new Error(
                    "SaaSPayment.InvalidReviewResolution",
                    "Resolution must be Refunded, BenefitApplied or NoActionNeeded."
                )
            );

        ReviewResolvedAtUtc = nowUtc;
        ReviewResolvedByUserId = actorUserId;
        ReviewResolution = resolution;
        return Result.Success();
    }

    private long SumOfRefunds()
    {
        long total = 0;
        foreach (var line in _refunds)
            total += line.Amount.AmountCents;
        return total;
    }

    /// <summary>Foto del pago tal como está ahora; la llevan los eventos de dominio de resultado.</summary>
    public SaaSPaymentSnapshot Snapshot() =>
        new(
            Id,
            TenantId,
            Type,
            Status,
            TargetAggregateId,
            IdempotencyKey.Value,
            Amount.AmountCents,
            Amount.Currency,
            ProviderCode,
            ExternalChargeReference?.Value,
            PaidAtUtc,
            FailureCode,
            FailureReason,
            NextRetryAtUtc,
            _attempts.Count,
            ProviderCheckoutSessionId is not null,
            CreatedBy,
            OnboardingId,
            CodeReservationId,
            CodeReservationPaymentId,
            DiscountAmountCents,
            PromotionSnapshotHash,
            Breakdown?.Quantity,
            Breakdown?.UnitAmountCents,
            Concept?.Code,
            Concept?.DisplayName
        );

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
