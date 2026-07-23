using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Aggregate root del proceso de firma electrónica. Encapsula:
/// <list type="bullet">
///   <item>Metadata de la solicitud (título, categoría, expiración, canales permitidos).</item>
///   <item>Referencia al documento original en CloudStorage (<c>OriginalFileId</c>).</item>
///   <item>Los firmantes y sus campos.</item>
///   <item>El ciclo de vida (<see cref="SignatureRequestStatus"/>).</item>
///   <item>El <c>RevocationEpoch</c> que invalida masivamente tokens públicos vigentes.</item>
/// </list>
///
/// <para>
/// Reglas de encapsulamiento: los <see cref="Signer"/>s y <see cref="SignatureField"/>s
/// sólo se crean/mutan a través de métodos del root. Nada expone <c>List&lt;T&gt;</c>
/// mutable — sólo <c>IReadOnlyList&lt;T&gt;</c>.
/// </para>
///
/// <para>
/// Ninguna transición usa un <c>Update(patch)</c> genérico; cada mutación tiene su método
/// explícito con su regla concreta. Cada método privado tiene un propósito único.
/// </para>
/// </summary>
public sealed class SignatureRequest : TenantEntity, IHasOwner
{
    public const int MinTitleLength = 3;
    public const int MaxTitleLength = 300;
    public const int MaxDescriptionLength = 2000;
    public const int MinSigners = 1;
    public const int MaxSigners = 50;

    private readonly List<Signer> _signers = [];

    private SignatureRequest() { }

    public Guid CreatedByUserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public SignatureCategory Category { get; private set; }
    public SignatureRequestStatus Status { get; private set; }

    public Guid OriginalFileId { get; private set; }
    public DocumentHash? DocumentHashPre { get; private set; }

    public Guid? SealedFileId { get; private set; }
    public DocumentHash? DocumentHashPost { get; private set; }
    public Guid? CertificateFileId { get; private set; }

    public bool RequiresSequentialSigning { get; private set; }
    public bool RequiresConsent { get; private set; }
    public bool GenerateCertificate { get; private set; }

    /// <summary>
    /// Hash del Practitioner PIN. Cuando != <c>null</c> el firmante debe superar el
    /// reto de PIN (<see cref="VerifySignerWithPin"/>) antes de <see cref="MarkSignerSigned"/>.
    /// El valor en claro nunca vive en el dominio — sólo el hash producido por Infrastructure.
    /// </summary>
    public string? PractitionerPinHash { get; private set; }
    public Guid? PractitionerPinSetByUserId { get; private set; }
    public DateTime? PractitionerPinSetAtUtc { get; private set; }

    /// <summary>Deriva del hash: <c>true</c> si el flujo público exige verificación por PIN antes de firmar.</summary>
    public bool RequiresPractitionerPin => PractitionerPinHash is not null;

    /// <summary>
    /// Identidad del preparer que aparece en el documento (opcional). Cuando != <c>null</c>
    /// el preparer debe firmar internamente vía <see cref="MarkPreparerSigned"/> — no usa
    /// token público. La firma del preparer no cuenta hacia <c>AllSignersHaveSigned</c>;
    /// vive en un canal paralelo, específico de los formularios que la requieren (Form 8879).
    /// </summary>
    public PreparerInfo? Preparer { get; private set; }
    public Guid? PreparerSignedByUserId { get; private set; }
    public DateTime? PreparerSignedAtUtc { get; private set; }
    public bool IsPreparerSigned => PreparerSignedByUserId is not null;

    public int TokenExpirationHours { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Contador monotónico incremental. Invalidar todos los tokens vigentes = incrementar.</summary>
    public int RevocationEpoch { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CanceledAtUtc { get; private set; }
    public DateTime? ExpiredAtUtc { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public Guid? RejectedBySignerId { get; private set; }

    /// <summary>Timestamp de la última tanda de reminders emitida por el scheduler.</summary>
    public DateTime? LastReminderSentAtUtc { get; private set; }

    /// <summary>Contador de reminders emitidos — cap útil para no spammear al firmante.</summary>
    public int RemindersSent { get; private set; }

    /// <summary>Legal hold activo — el PurgeScheduler NO purga la solicitud mientras esté en <c>true</c>.</summary>
    public bool LegalHold { get; private set; }

    /// <summary>Motivo textual (subpoena #, ticket legal) del hold. Solo se lee por el staff con permiso RequestRead.</summary>
    public string? LegalHoldReason { get; private set; }

    public Guid? LegalHoldPlacedByUserId { get; private set; }
    public DateTime? LegalHoldPlacedAtUtc { get; private set; }
    public Guid? LegalHoldLiftedByUserId { get; private set; }
    public DateTime? LegalHoldLiftedAtUtc { get; private set; }

    public IReadOnlyList<Signer> Signers => _signers.AsReadOnly();

    // ------------------------------------------------------------------
    // Factory
    // ------------------------------------------------------------------

    public static Result<SignatureRequest> CreateDraft(
        Guid tenantId,
        Guid createdByUserId,
        string title,
        string? description,
        SignatureCategory category,
        Guid originalFileId,
        int tokenExpirationHours,
        bool requiresSequentialSigning,
        bool requiresConsent,
        bool generateCertificate
    )
    {
        var baseValidation = ValidateFactoryInputs(
            tenantId,
            createdByUserId,
            title,
            description,
            originalFileId,
            tokenExpirationHours
        );
        if (baseValidation.IsFailure)
            return Result.Failure<SignatureRequest>(baseValidation.Error);

        var now = DateTime.UtcNow;
        var request = new SignatureRequest
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = createdByUserId,
            Title = title.Trim(),
            Description = NormalizeDescription(description),
            Category = category,
            Status = SignatureRequestStatus.Draft,
            OriginalFileId = originalFileId,
            TokenExpirationHours = tokenExpirationHours,
            ExpiresAtUtc = now.AddHours(tokenExpirationHours),
            RequiresSequentialSigning = requiresSequentialSigning,
            RequiresConsent = requiresConsent,
            GenerateCertificate = generateCertificate,
            RevocationEpoch = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        request.SetTenant(tenantId);
        return Result.Success(request);
    }

    // ------------------------------------------------------------------
    // Signers — cada operación con SU regla
    // ------------------------------------------------------------------

    public Result<Signer> AddSigner(
        SignerEmail email,
        SignerFullName fullName,
        Guid? mappedCustomerId,
        SignerPhoneNumber? phoneNumber = null
    )
    {
        EnsureCanBeEdited();

        if (_signers.Count >= MaxSigners)
            return Result.Failure<Signer>(
                new Error("Signature.Request.TooManySigners", $"Signer count cannot exceed {MaxSigners}.")
            );

        if (EmailAlreadyPresent(email))
            return Result.Failure<Signer>(
                new Error("Signature.Request.DuplicateSignerEmail", "This email is already registered as a signer.")
            );

        var order = NextOrder();
        var signerResult = Signer.Create(Id, email, fullName, mappedCustomerId, order, phoneNumber);
        if (signerResult.IsFailure)
            return signerResult;

        _signers.Add(signerResult.Value);
        Touch();
        return signerResult;
    }

    /// <summary>Actualiza el teléfono del firmante en <c>Draft</c>/<c>Ready</c>. Solo permitido antes de Send.</summary>
    public Result SetSignerPhoneNumber(Guid signerId, SignerPhoneNumber? phoneNumber)
    {
        EnsureCanBeEdited();
        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        var result = signer.SetPhoneNumber(phoneNumber);
        if (result.IsFailure)
            return result;
        Touch();
        return Result.Success();
    }

    public Result RemoveSigner(Guid signerId)
    {
        EnsureCanBeEdited();

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        _signers.Remove(signer);
        NormalizeSignerOrder();
        Touch();
        return Result.Success();
    }

    public Result ReorderSigners(IReadOnlyList<Guid> orderedSignerIds)
    {
        ArgumentNullException.ThrowIfNull(orderedSignerIds);

        EnsureCanBeEdited();

        if (orderedSignerIds.Count != _signers.Count)
            return Result.Failure(
                new Error(
                    "Signature.Request.ReorderMismatch",
                    "The provided order does not match the current signer count."
                )
            );

        var lookup = _signers.ToDictionary(s => s.Id);
        var applied = new List<Signer>(orderedSignerIds.Count);
        for (var i = 0; i < orderedSignerIds.Count; i++)
        {
            if (!lookup.TryGetValue(orderedSignerIds[i], out var signer))
                return Result.Failure(
                    new Error("Signature.Request.ReorderUnknownSigner", "Unknown signer id in the requested order.")
                );

            var reorderResult = signer.Reorder(i + 1);
            if (reorderResult.IsFailure)
                return reorderResult;

            applied.Add(signer);
        }

        _signers.Clear();
        _signers.AddRange(applied);
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Fields — placement por firmante
    // ------------------------------------------------------------------

    public Result<SignatureField> PlaceField(
        Guid signerId,
        SignatureFieldKind kind,
        FieldPosition position,
        string? label,
        bool isRequired
    )
    {
        EnsureCanBeEdited();

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure<SignatureField>(
                new Error("Signature.Request.SignerMissing", "Cannot place a field on an unknown signer.")
            );

        var fieldResult = SignatureField.Create(Id, signerId, kind, position, label, isRequired);
        if (fieldResult.IsFailure)
            return fieldResult;

        var addResult = signer.AddField(fieldResult.Value);
        if (addResult.IsFailure)
            return Result.Failure<SignatureField>(addResult.Error);

        Touch();
        return fieldResult;
    }

    public Result RemoveField(Guid signerId, Guid fieldId)
    {
        EnsureCanBeEdited();

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        var removeResult = signer.RemoveField(fieldId);
        if (removeResult.IsFailure)
            return removeResult;

        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Progresión de estado
    // ------------------------------------------------------------------

    /// <summary>
    /// Marca el request como <c>Ready</c> cuando <c>OriginalFileId</c> confirmó estar
    /// disponible en CloudStorage (proyección local <c>FileMetadataRef</c>).
    /// </summary>
    public Result MarkReadyForSending(DocumentHash originalHash)
    {
        ArgumentNullException.ThrowIfNull(originalHash);

        if (Status != SignatureRequestStatus.Draft)
            return Result.Failure(
                new Error("Signature.Request.NotDraft", "Only a Draft request can transition to Ready.")
            );

        DocumentHashPre = originalHash;
        Status = SignatureRequestStatus.Ready;
        Touch();
        return Result.Success();
    }

    /// <summary>Transiciona <c>Ready → InProgress</c> al enviar la solicitud.</summary>
    public Result Send(DateTime sentAtUtc)
    {
        if (Status != SignatureRequestStatus.Ready)
            return Result.Failure(new Error("Signature.Request.NotReady", "Only a Ready request can be sent."));

        if (_signers.Count < MinSigners)
            return Result.Failure(
                new Error("Signature.Request.NoSigners", "The request must have at least one signer.")
            );

        if (!HasAnyRequiredSignatureField())
            return Result.Failure(
                new Error(
                    "Signature.Request.NoSignatureField",
                    "The request must have at least one Signature or Initials field placed."
                )
            );

        Status = SignatureRequestStatus.InProgress;
        SentAtUtc = sentAtUtc;
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Preparer — identity + firma interna del staff (Form 8879, POA, etc.)
    // ------------------------------------------------------------------

    /// <summary>
    /// Asigna el preparer a la solicitud. Sólo permitido en <c>Draft</c> o <c>Ready</c>.
    /// Reasignar en <c>Draft/Ready</c> reinicia el estado de firma del preparer.
    /// </summary>
    public Result SetPreparer(PreparerInfo preparer)
    {
        ArgumentNullException.ThrowIfNull(preparer);
        EnsureCanBeEdited();

        Preparer = preparer;
        PreparerSignedByUserId = null;
        PreparerSignedAtUtc = null;
        Touch();
        return Result.Success();
    }

    /// <summary>Quita el preparer asignado. Sólo permitido en <c>Draft</c> o <c>Ready</c>.</summary>
    public Result ClearPreparer()
    {
        EnsureCanBeEdited();

        Preparer = null;
        PreparerSignedByUserId = null;
        PreparerSignedAtUtc = null;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Registra la firma interna del preparer con las credenciales del usuario staff
    /// autenticado. No requiere token público. Idempotente cuando ya está firmado por
    /// el mismo usuario. Se permite sólo cuando la solicitud está <c>InProgress</c> o
    /// <c>Completed</c> — típicamente el preparer firma tras aprobar el taxpayer.
    /// </summary>
    public Result MarkPreparerSigned(Guid preparerUserId, DateTime signedAtUtc, string? clientIp, string? userAgent)
    {
        if (preparerUserId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.PreparerUser", "PreparerUserId is required."));

        if (Preparer is null)
            return Result.Failure(
                new Error("Signature.Request.NoPreparer", "This request does not have a preparer assigned.")
            );

        if (Status is not (SignatureRequestStatus.InProgress or SignatureRequestStatus.Completed))
            return Result.Failure(
                new Error("Signature.Request.PreparerStatus", "Preparer can sign only after the request is InProgress.")
            );

        if (IsPreparerSigned && PreparerSignedByUserId == preparerUserId)
            return Result.Success();

        if (IsPreparerSigned)
            return Result.Failure(
                new Error("Signature.Request.PreparerAlreadySigned", "Preparer signature already recorded.")
            );

        PreparerSignedByUserId = preparerUserId;
        PreparerSignedAtUtc = signedAtUtc;
        _ = clientIp; // no lo almacenamos aquí: es contexto staff autenticado
        _ = userAgent;
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Practitioner PIN — set/clear por staff, verify por firmante público
    // ------------------------------------------------------------------

    /// <summary>
    /// Asigna el hash del Practitioner PIN. Sólo permitido en <c>Draft</c> o <c>Ready</c>
    /// (no reasignable una vez enviada la solicitud — evita cambios en curso que rompan
    /// tokens vigentes). Reasignar en Draft/Ready reinicia los intentos y desbloqueos
    /// previos de todos los firmantes.
    /// </summary>
    public Result SetPractitionerPin(string pinHash, Guid setByUserId, DateTime setAtUtc)
    {
        if (string.IsNullOrWhiteSpace(pinHash))
            return Result.Failure(new Error("Signature.Request.PinHash", "PIN hash is required."));
        if (setByUserId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.PinSetter", "SetByUserId is required."));

        EnsureCanBeEdited();

        PractitionerPinHash = pinHash;
        PractitionerPinSetByUserId = setByUserId;
        PractitionerPinSetAtUtc = setAtUtc;
        Touch();
        return Result.Success();
    }

    /// <summary>Quita el requerimiento de PIN. Sólo permitido en <c>Draft</c> o <c>Ready</c>.</summary>
    public Result ClearPractitionerPin()
    {
        EnsureCanBeEdited();

        PractitionerPinHash = null;
        PractitionerPinSetByUserId = null;
        PractitionerPinSetAtUtc = null;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Verifica el PIN de un firmante. El <paramref name="isMatch"/> lo produce la capa
    /// Infrastructure comparando en tiempo constante el hash almacenado contra el PIN
    /// enviado por el firmante — el dominio no ve el valor en claro ni el hash directamente.
    ///
    /// <para>Reglas:</para>
    /// <list type="bullet">
    ///   <item>Sólo se acepta en <c>InProgress</c>.</item>
    ///   <item>Falla si no hay PIN configurado.</item>
    ///   <item>Falla si el firmante está bloqueado por intentos previos.</item>
    ///   <item>Es idempotente cuando ya está verificado.</item>
    ///   <item>Si <paramref name="isMatch"/> es <c>false</c> incrementa el contador de fallos
    ///     y — al alcanzar <see cref="Signer.MaxPinAttempts"/> — bloquea al firmante.</item>
    /// </list>
    /// </summary>
    public Result VerifySignerWithPin(
        Guid signerId,
        bool isMatch,
        DateTime attemptedAtUtc,
        string? clientIp,
        string? userAgent
    )
    {
        if (Status != SignatureRequestStatus.InProgress)
            return Result.Failure(
                new Error("Signature.Request.NotInProgress", "Only an InProgress request can accept PIN verification.")
            );

        if (!RequiresPractitionerPin)
            return Result.Failure(
                new Error("Signature.Request.PinNotConfigured", "This request does not require a practitioner PIN.")
            );

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        if (signer.IsPinLockedAt(attemptedAtUtc))
            return Result.Failure(
                new Error(
                    "Signature.Signer.PinLocked",
                    "The signer is temporarily locked after too many failed attempts."
                )
            );

        if (signer.IsPinVerified)
        {
            Touch();
            return Result.Success();
        }

        if (isMatch)
        {
            var recordResult = signer.RecordPinVerified(attemptedAtUtc, clientIp, userAgent);
            if (recordResult.IsFailure)
                return recordResult;
            Touch();
            return Result.Success();
        }

        signer.RecordPinFailedAttempt(attemptedAtUtc, clientIp, userAgent);
        Touch();
        return Result.Failure(new Error("Signature.Signer.PinMismatch", "The PIN provided is incorrect."));
    }

    // ------------------------------------------------------------------
    // Verification framework genérico (SMS/Email/WhatsApp/KBA)
    // Los canales concretos se conectan como consumers externos del evento
    // SignerVerificationChallengeIssuedIntegrationEvent — Signature no conoce
    // los proveedores concretos (Twilio, MessageBird, etc.).
    // ------------------------------------------------------------------

    /// <summary>
    /// Emite un challenge de verificación para el firmante en el método indicado. El
    /// <paramref name="answerHash"/> lo produce Application con el mismo hasher que el PIN
    /// (PBKDF2). La Application también publica el evento externo con el valor en claro.
    /// </summary>
    /// <summary>
    /// Cooldown mínimo entre challenges consecutivos del mismo método — evita spam
    /// (resend/switch-channel abuse) del endpoint público.
    /// </summary>
    public static readonly TimeSpan ChallengeResendCooldown = TimeSpan.FromSeconds(30);

    public Result<SignerVerificationChallenge> IssueVerificationChallenge(
        Guid signerId,
        SignerVerificationMethod method,
        string answerHash,
        DateTime issuedAtUtc,
        TimeSpan lifetime
    )
    {
        if (Status != SignatureRequestStatus.InProgress)
            return Result.Failure<SignerVerificationChallenge>(
                new Error(
                    "Signature.Request.NotInProgress",
                    "Only an InProgress request can issue verification challenges."
                )
            );
        if (method == SignerVerificationMethod.PractitionerPin)
            return Result.Failure<SignerVerificationChallenge>(
                new Error(
                    "Signature.Request.PinNotChallenge",
                    "PractitionerPin is not a per-signer challenge; use SetPractitionerPin."
                )
            );

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure<SignerVerificationChallenge>(
                new Error("Signature.Request.SignerMissing", "Signer not found in this request.")
            );

        // Cooldown: rechaza si el firmante pidió otro challenge del MISMO método hace
        // menos de <c>ChallengeResendCooldown</c>. Cambiar de método NO tiene cooldown
        // (soporta switch-channel inmediato).
        var recent = signer.CurrentChallengeFor(method, issuedAtUtc);
        if (recent is not null && (issuedAtUtc - recent.IssuedAtUtc) < ChallengeResendCooldown)
            return Result.Failure<SignerVerificationChallenge>(
                new Error("Signature.Signer.ChallengeCooldown", "Please wait before requesting another code.")
            );

        if (RequiresPhoneNumberFor(method) && signer.PhoneNumber is null)
            return Result.Failure<SignerVerificationChallenge>(
                new Error(
                    "Signature.Signer.PhoneMissing",
                    "This verification method requires the signer to have a phone number."
                )
            );

        var expiresAt = issuedAtUtc.Add(lifetime);
        var challengeResult = SignerVerificationChallenge.Create(signer.Id, method, answerHash, issuedAtUtc, expiresAt);
        if (challengeResult.IsFailure)
            return challengeResult;

        var attachResult = signer.AttachChallenge(challengeResult.Value, issuedAtUtc);
        if (attachResult.IsFailure)
            return Result.Failure<SignerVerificationChallenge>(attachResult.Error);

        Touch();
        return challengeResult;
    }

    /// <summary>
    /// Verifica la respuesta que envió el firmante contra el challenge activo del método
    /// indicado. <paramref name="isMatch"/> lo produce Application comparando en tiempo
    /// constante. La regla de lockout se apoya en el contador genérico del Signer.
    /// </summary>
    public Result VerifyVerificationChallenge(
        Guid signerId,
        SignerVerificationMethod method,
        bool isMatch,
        DateTime attemptedAtUtc,
        string? clientIp,
        string? userAgent
    )
    {
        if (Status != SignatureRequestStatus.InProgress)
            return Result.Failure(
                new Error("Signature.Request.NotInProgress", "Only an InProgress request can accept verification.")
            );
        if (method == SignerVerificationMethod.PractitionerPin)
            return Result.Failure(
                new Error("Signature.Request.PinNotChallenge", "Use VerifySignerWithPin for PractitionerPin.")
            );

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        if (signer.IsPinLockedAt(attemptedAtUtc))
            return Result.Failure(
                new Error(
                    "Signature.Signer.PinLocked",
                    "The signer is temporarily locked after too many failed attempts."
                )
            );

        var current = signer.CurrentChallengeFor(method, attemptedAtUtc);
        if (current is null)
            return Result.Failure(
                new Error("Signature.Signer.NoActiveChallenge", "No active challenge for this method.")
            );

        if (isMatch)
        {
            var consume = signer.ConsumeChallenge(current, attemptedAtUtc);
            if (consume.IsFailure)
                return consume;
            signer.RecordPinVerified(attemptedAtUtc, clientIp, userAgent);
            Touch();
            return Result.Success();
        }

        signer.RecordPinFailedAttempt(attemptedAtUtc, clientIp, userAgent);
        Touch();
        return Result.Failure(new Error("Signature.Signer.ChallengeMismatch", "The verification code is incorrect."));
    }

    private static bool RequiresPhoneNumberFor(SignerVerificationMethod method) =>
        method is SignerVerificationMethod.SmsOtp or SignerVerificationMethod.WhatsAppOtp;

    /// <summary>
    /// Registra la aceptación del consent para un firmante concreto. Idempotente.
    /// Solo aplica cuando la solicitud tiene <c>RequiresConsent = true</c>; caso
    /// contrario devuelve error para no ocultar ambigüedades semánticas al caller.
    /// </summary>
    public Result AcceptSignerConsent(Guid signerId, DateTime acceptedAtUtc, string? clientIp, string? userAgent)
    {
        if (Status != SignatureRequestStatus.InProgress)
            return Result.Failure(
                new Error("Signature.Request.NotInProgress", "Only an InProgress request can capture consent.")
            );

        if (!RequiresConsent)
            return Result.Failure(
                new Error("Signature.Request.ConsentNotRequired", "This request does not require consent.")
            );

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        var recordResult = signer.RecordConsentAcceptance(acceptedAtUtc, clientIp, userAgent);
        if (recordResult.IsFailure)
            return recordResult;

        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Marca la primera apertura del enlace público por el firmante. Idempotente.
    /// Aplica en cualquier estado no terminal (Draft/Ready/InProgress) para no perder
    /// la trazabilidad si la vista ocurre antes de Send.
    /// </summary>
    public Result RecordSignerFirstView(Guid signerId, DateTime viewedAtUtc, string? clientIp, string? userAgent)
    {
        if (IsTerminal())
            return Result.Failure(new Error("Signature.Request.Terminal", "Cannot record view on a terminal request."));

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        signer.RecordFirstView(viewedAtUtc, clientIp, userAgent);
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Registra la firma de un signer específico y transiciona a <c>Completed</c> si es
    /// el último pendiente. Idempotente.
    /// </summary>
    public Result MarkSignerSigned(Guid signerId, DateTime signedAtUtc, string? clientIp, string? userAgent) =>
        MarkSignerSigned(
            signerId,
            signedAtUtc,
            SignatureCaptureMethod.Typed,
            typedName: null,
            signatureImageFileId: null,
            clientIp,
            userAgent
        );

    /// <summary>
    /// Marca al firmante como Signed capturando el método (Typed/Drawn/Uploaded) y la
    /// evidencia asociada (typed name o FileId de imagen). Transiciona a Completed si es
    /// el último firmante. Idempotente si el firmante ya está Signed.
    /// </summary>
    public Result MarkSignerSigned(
        Guid signerId,
        DateTime signedAtUtc,
        SignatureCaptureMethod captureMethod,
        string? typedName,
        Guid? signatureImageFileId,
        string? clientIp,
        string? userAgent
    )
    {
        if (Status != SignatureRequestStatus.InProgress)
            return Result.Failure(
                new Error("Signature.Request.NotInProgress", "Only an InProgress request can accept signatures.")
            );

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        if (RequiresConsent && !signer.HasAcceptedConsent)
            return Result.Failure(
                new Error("Signature.Request.ConsentRequired", "Signer must accept the consent before signing.")
            );

        if (RequiresPractitionerPin && !signer.IsPinVerified)
            return Result.Failure(
                new Error(
                    "Signature.Request.PinVerificationRequired",
                    "Signer must verify the practitioner PIN before signing."
                )
            );

        if (RequiresSequentialSigning && !IsSignerNextInSequence(signer))
            return Result.Failure(
                new Error(
                    "Signature.Request.NotYourTurn",
                    "This request is sequential and it is not this signer's turn yet."
                )
            );

        var recordResult = signer.RecordSigned(
            signedAtUtc,
            captureMethod,
            typedName,
            signatureImageFileId,
            clientIp,
            userAgent
        );
        if (recordResult.IsFailure)
            return recordResult;

        if (AllSignersHaveSigned())
            TransitionToCompleted(signedAtUtc);
        else
            Touch();

        return Result.Success();
    }

    public Result MarkSignerRejected(
        Guid signerId,
        DateTime rejectedAtUtc,
        string? reason,
        string? clientIp,
        string? userAgent
    )
    {
        if (Status != SignatureRequestStatus.InProgress)
            return Result.Failure(
                new Error("Signature.Request.NotInProgress", "Only an InProgress request can be rejected by a signer.")
            );

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        var recordResult = signer.RecordRejected(rejectedAtUtc, reason, clientIp, userAgent);
        if (recordResult.IsFailure)
            return recordResult;

        Status = SignatureRequestStatus.Rejected;
        RejectedAtUtc = rejectedAtUtc;
        RejectedBySignerId = signerId;
        BumpRevocationEpoch();
        Touch();
        return Result.Success();
    }

    public Result Cancel(DateTime canceledAtUtc)
    {
        if (IsTerminal())
            return Result.Failure(new Error("Signature.Request.Terminal", "Cannot cancel a terminal request."));

        Status = SignatureRequestStatus.Canceled;
        CanceledAtUtc = canceledAtUtc;
        BumpRevocationEpoch();
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Legal hold (Fase 9) — bloquea purga por retention hasta que se levante
    // ------------------------------------------------------------------

    public const int MaxLegalHoldReasonLength = 500;

    /// <summary>Coloca un legal hold. Idempotente: reasignar por el mismo usuario refresca timestamp.</summary>
    public Result PlaceLegalHold(Guid placedByUserId, string reason)
    {
        if (placedByUserId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.LegalHoldUser", "PlacedByUserId is required."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Signature.Request.LegalHoldReason", "Legal hold reason is required."));
        var trimmed = reason.Trim();
        if (trimmed.Length > MaxLegalHoldReasonLength)
            return Result.Failure(
                new Error(
                    "Signature.Request.LegalHoldReasonLength",
                    $"Reason cannot exceed {MaxLegalHoldReasonLength} chars."
                )
            );

        LegalHold = true;
        LegalHoldReason = trimmed;
        LegalHoldPlacedByUserId = placedByUserId;
        LegalHoldPlacedAtUtc = DateTime.UtcNow;
        LegalHoldLiftedByUserId = null;
        LegalHoldLiftedAtUtc = null;
        Touch();
        return Result.Success();
    }

    /// <summary>Levanta el legal hold. Idempotente si ya estaba levantado.</summary>
    public Result LiftLegalHold(Guid liftedByUserId)
    {
        if (liftedByUserId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.LegalHoldUser", "LiftedByUserId is required."));
        if (!LegalHold)
            return Result.Success();

        LegalHold = false;
        LegalHoldLiftedByUserId = liftedByUserId;
        LegalHoldLiftedAtUtc = DateTime.UtcNow;
        Touch();
        return Result.Success();
    }

    /// <summary>Registra que se emitió una tanda de reminders. No transiciona estado — solo actualiza contadores.</summary>
    public Result RecordReminderDispatched(DateTime sentAtUtc)
    {
        if (IsTerminal())
            return Result.Failure(
                new Error("Signature.Request.Terminal", "Cannot dispatch reminders on a terminal request.")
            );
        if (Status != SignatureRequestStatus.InProgress)
            return Result.Failure(
                new Error("Signature.Request.NotInProgress", "Only InProgress requests receive reminders.")
            );

        LastReminderSentAtUtc = sentAtUtc;
        RemindersSent++;
        Touch();
        return Result.Success();
    }

    public Result MarkExpired(DateTime expiredAtUtc)
    {
        if (IsTerminal())
            return Result.Failure(new Error("Signature.Request.Terminal", "Cannot expire a terminal request."));

        Status = SignatureRequestStatus.Expired;
        ExpiredAtUtc = expiredAtUtc;
        foreach (var signer in _signers)
            signer.RecordExpired();

        BumpRevocationEpoch();
        Touch();
        return Result.Success();
    }

    public Result ExtendExpiration(int additionalHours)
    {
        if (IsTerminal())
            return Result.Failure(new Error("Signature.Request.Terminal", "Cannot extend a terminal request."));

        if (additionalHours is < 1 or > 720)
            return Result.Failure(
                new Error("Signature.Request.ExtendRange", "Additional hours must be between 1 and 720.")
            );

        ExpiresAtUtc = ExpiresAtUtc.AddHours(additionalHours);
        BumpRevocationEpoch();
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Sealing (Fase 4) — reservado para completar cuando exista el worker
    // ------------------------------------------------------------------

    /// <summary>
    /// Registra en una sola transición el resultado del sealing (archivo sellado, hash
    /// post y — opcionalmente — el Certificate of Completion). Idempotente: si el
    /// mismo <paramref name="sealedFileId"/> ya está registrado, devuelve éxito sin
    /// duplicar cambios.
    /// </summary>
    public Result MarkSealed(Guid sealedFileId, DocumentHash sealedHash, Guid? certificateFileId)
    {
        ArgumentNullException.ThrowIfNull(sealedHash);

        if (Status != SignatureRequestStatus.Completed)
            return Result.Failure(
                new Error(
                    "Signature.Request.NotCompleted",
                    "Sealed document can only be attached to a completed request."
                )
            );

        if (sealedFileId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.SealedFile", "SealedFileId is required."));

        if (SealedFileId == sealedFileId && CertificateFileId == certificateFileId)
            return Result.Success();

        SealedFileId = sealedFileId;
        DocumentHashPost = sealedHash;
        if (certificateFileId is not null)
        {
            if (certificateFileId == Guid.Empty)
                return Result.Failure(
                    new Error("Signature.Request.CertificateFile", "CertificateFileId cannot be an empty Guid.")
                );
            CertificateFileId = certificateFileId;
        }

        Touch();
        return Result.Success();
    }

    public Result RecordSealedDocument(Guid sealedFileId, DocumentHash sealedHash)
    {
        ArgumentNullException.ThrowIfNull(sealedHash);

        if (Status != SignatureRequestStatus.Completed)
            return Result.Failure(
                new Error(
                    "Signature.Request.NotCompleted",
                    "Sealed document can only be attached to a completed request."
                )
            );

        if (sealedFileId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.SealedFile", "SealedFileId is required."));

        SealedFileId = sealedFileId;
        DocumentHashPost = sealedHash;
        Touch();
        return Result.Success();
    }

    public Result RecordCertificate(Guid certificateFileId)
    {
        if (Status != SignatureRequestStatus.Completed)
            return Result.Failure(
                new Error("Signature.Request.NotCompleted", "Certificate can only be attached to a completed request.")
            );

        if (certificateFileId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.CertificateFile", "CertificateFileId is required."));

        CertificateFileId = certificateFileId;
        Touch();
        return Result.Success();
    }

    // ==================================================================
    // Helpers privados — cada uno con propósito único
    // ==================================================================

    private static Result ValidateFactoryInputs(
        Guid tenantId,
        Guid createdByUserId,
        string title,
        string? description,
        Guid originalFileId,
        int tokenExpirationHours
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.Tenant", "TenantId is required."));

        if (createdByUserId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.CreatedBy", "CreatedByUserId is required."));

        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure(new Error("Signature.Request.Title", "Title is required."));

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length is < MinTitleLength or > MaxTitleLength)
            return Result.Failure(
                new Error(
                    "Signature.Request.Title",
                    $"Title must be between {MinTitleLength} and {MaxTitleLength} characters."
                )
            );

        if (description is not null && description.Length > MaxDescriptionLength)
            return Result.Failure(
                new Error(
                    "Signature.Request.Description",
                    $"Description cannot exceed {MaxDescriptionLength} characters."
                )
            );

        if (originalFileId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.OriginalFile", "OriginalFileId is required."));

        if (tokenExpirationHours is < 1 or > 720)
            return Result.Failure(
                new Error("Signature.Request.TokenExpiration", "Token expiration must be between 1 and 720 hours.")
            );

        return Result.Success();
    }

    private void EnsureCanBeEdited()
    {
        if (Status is not (SignatureRequestStatus.Draft or SignatureRequestStatus.Ready))
            throw new InvalidOperationException($"SignatureRequest {Id} cannot be edited in status {Status}.");
    }

    private bool EmailAlreadyPresent(SignerEmail email) => _signers.Any(s => s.Email.Value == email.Value);

    private int NextOrder() => _signers.Count == 0 ? 1 : _signers.Max(s => s.Order) + 1;

    private void NormalizeSignerOrder()
    {
        var ordered = _signers.OrderBy(s => s.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Reorder(i + 1);

        _signers.Clear();
        _signers.AddRange(ordered);
    }

    private Signer? FindSignerOrNull(Guid signerId) => _signers.Find(s => s.Id == signerId);

    private bool HasAnyRequiredSignatureField() =>
        _signers.Any(s => s.Fields.Any(f => f.Kind is SignatureFieldKind.Signature or SignatureFieldKind.Initials));

    private bool AllSignersHaveSigned() => _signers.Count > 0 && _signers.All(s => s.Status == SignerStatus.Signed);

    /// <summary>
    /// Determina si <paramref name="signer"/> es el próximo pendiente cuando la solicitud
    /// exige firma secuencial. Considera "próximo" al de menor <c>Order</c> que aún no
    /// haya firmado ni rechazado.
    /// </summary>
    private bool IsSignerNextInSequence(Signer signer)
    {
        var next = _signers.Where(s => s.Status == SignerStatus.Pending).OrderBy(s => s.Order).FirstOrDefault();
        return next is not null && next.Id == signer.Id;
    }

    private bool IsTerminal() =>
        Status
            is SignatureRequestStatus.Completed
                or SignatureRequestStatus.Rejected
                or SignatureRequestStatus.Canceled
                or SignatureRequestStatus.Expired;

    private void TransitionToCompleted(DateTime completedAtUtc)
    {
        Status = SignatureRequestStatus.Completed;
        CompletedAtUtc = completedAtUtc;
        BumpRevocationEpoch();
        Touch();
    }

    private void BumpRevocationEpoch() => RevocationEpoch++;

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        return description.Trim();
    }
}
