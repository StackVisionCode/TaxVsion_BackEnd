using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Firmante dentro de una <see cref="SignatureRequest"/>. Entidad interna del aggregate
/// root: sólo se crea/muta via métodos del root (<c>AddSigner</c>, <c>RecordSignedBy</c>,
/// <c>RecordRejectionBy</c>, etc.).
///
/// Puede ser cliente registrado del tenant (<see cref="MappedCustomerId"/>) o externo
/// (sólo email + nombre). El matcheo contra <c>CustomerEmailProjection</c> lo hace la
/// capa Application.
/// </summary>
public sealed class Signer : BaseEntity
{
    private readonly List<SignatureField> _fields = [];
    private readonly List<SignerVerificationChallenge> _challenges = [];

    private Signer() { }

    public Guid SignatureRequestId { get; private set; }
    public SignerEmail Email { get; private set; } = default!;
    public SignerFullName FullName { get; private set; } = default!;

    /// <summary>Teléfono opcional del firmante (necesario para SMS/WhatsApp OTP).</summary>
    public SignerPhoneNumber? PhoneNumber { get; private set; }

    /// <summary>Vinculación opcional a un cliente registrado (matcheo por email).</summary>
    public Guid? MappedCustomerId { get; private set; }

    /// <summary>Orden 1-indexado. Sólo aplica cuando la solicitud es secuencial.</summary>
    public int Order { get; private set; }

    public SignerStatus Status { get; private set; }
    public DateTime? SignedAtUtc { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public string? RejectReason { get; private set; }
    public string? ClientIp { get; private set; }
    public string? UserAgent { get; private set; }

    /// <summary>Marca si el firmante ya aceptó el disclosure/consent (aplica cuando la solicitud lo exige).</summary>
    public bool HasAcceptedConsent { get; private set; }
    public DateTime? ConsentAcceptedAtUtc { get; private set; }

    /// <summary>Timestamp de la primera apertura del enlace público por el firmante (audit trail).</summary>
    public DateTime? FirstViewedAtUtc { get; private set; }

    /// <summary>Método de captura de la firma (Typed/Drawn/Uploaded). <c>null</c> hasta que el firmante firme.</summary>
    public SignatureCaptureMethod? CaptureMethod { get; private set; }

    /// <summary>Nombre tecleado por el firmante (aplica cuando <see cref="CaptureMethod"/> = Typed).</summary>
    public string? TypedName { get; private set; }

    /// <summary>FileId en CloudStorage de la imagen dibujada o subida (aplica cuando Drawn/Uploaded).</summary>
    public Guid? SignatureImageFileId { get; private set; }

    /// <summary>Indica que el firmante superó el reto de PIN (si la solicitud lo exige).</summary>
    public bool IsPinVerified { get; private set; }
    public DateTime? PinVerifiedAtUtc { get; private set; }

    /// <summary>Intentos fallidos consecutivos de PIN. Se resetea tras un lock que expira.</summary>
    public int PinFailedAttempts { get; private set; }

    /// <summary>Si != null y &gt; <c>now</c>, el firmante está bloqueado hasta esta fecha.</summary>
    public DateTime? PinLockedUntilUtc { get; private set; }

    /// <summary>Vista de sólo lectura de los campos del firmante.</summary>
    public IReadOnlyList<SignatureField> Fields => _fields.AsReadOnly();

    /// <summary>Vista de sólo lectura de los challenges de verificación del firmante.</summary>
    public IReadOnlyList<SignerVerificationChallenge> Challenges => _challenges.AsReadOnly();

    /// <summary><c>true</c> si el firmante ya completó exitosamente el método indicado.</summary>
    public bool HasCompletedVerification(SignerVerificationMethod method) =>
        method switch
        {
            SignerVerificationMethod.PractitionerPin => IsPinVerified,
            _ => _challenges.Any(c => c.Method == method && c.IsConsumed),
        };

    // ------------------------------------------------------------------
    // Factory: llamado por el aggregate root únicamente.
    // ------------------------------------------------------------------

    internal static Result<Signer> Create(
        Guid requestId,
        SignerEmail email,
        SignerFullName fullName,
        Guid? mappedCustomerId,
        int order,
        SignerPhoneNumber? phoneNumber = null
    )
    {
        if (requestId == Guid.Empty)
            return Result.Failure<Signer>(new Error("Signature.Signer.Request", "SignatureRequestId is required."));

        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(fullName);

        if (order < 1)
            return Result.Failure<Signer>(new Error("Signature.Signer.Order", "Signer order must be >= 1."));

        return Result.Success(
            new Signer
            {
                Id = Guid.NewGuid(),
                SignatureRequestId = requestId,
                Email = email,
                FullName = fullName,
                PhoneNumber = phoneNumber,
                MappedCustomerId = mappedCustomerId,
                Order = order,
                Status = SignerStatus.Pending,
            }
        );
    }

    internal Result SetPhoneNumber(SignerPhoneNumber? phoneNumber)
    {
        EnsurePending();
        PhoneNumber = phoneNumber;
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Mutaciones específicas — cada una una sola regla.
    // ------------------------------------------------------------------

    internal Result Reorder(int newOrder)
    {
        if (newOrder < 1)
            return Result.Failure(new Error("Signature.Signer.Order", "Signer order must be >= 1."));

        Order = newOrder;
        return Result.Success();
    }

    internal Result RenameFullName(SignerFullName newFullName)
    {
        ArgumentNullException.ThrowIfNull(newFullName);
        EnsurePending();
        FullName = newFullName;
        return Result.Success();
    }

    internal Result MapToRegisteredCustomer(Guid customerId)
    {
        if (customerId == Guid.Empty)
            return Result.Failure(new Error("Signature.Signer.Customer", "CustomerId is required."));

        EnsurePending();
        MappedCustomerId = customerId;
        return Result.Success();
    }

    internal Result UnmapRegisteredCustomer()
    {
        EnsurePending();
        MappedCustomerId = null;
        return Result.Success();
    }

    internal Result AddField(SignatureField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.SignerId != Id)
            return Result.Failure(
                new Error("Signature.Signer.FieldOwnership", "Field does not belong to this signer.")
            );

        EnsurePending();
        _fields.Add(field);
        return Result.Success();
    }

    internal Result RemoveField(Guid fieldId)
    {
        EnsurePending();
        var field = _fields.Find(f => f.Id == fieldId);
        if (field is null)
            return Result.Failure(new Error("Signature.Signer.FieldMissing", "Field not found for this signer."));

        _fields.Remove(field);
        return Result.Success();
    }

    /// <summary>Marca al firmante como Signed. Idempotente.</summary>
    internal Result RecordSigned(DateTime signedAtUtc, string? clientIp, string? userAgent) =>
        RecordSigned(
            signedAtUtc,
            SignatureCaptureMethod.Typed,
            typedName: FullName.Value,
            signatureImageFileId: null,
            clientIp,
            userAgent
        );

    /// <summary>
    /// Marca al firmante como Signed capturando además la evidencia del método
    /// (typed name o FileId de imagen dibujada/subida). Regla P-24: cuando el método es
    /// Drawn/Uploaded es obligatorio proveer <paramref name="signatureImageFileId"/>;
    /// cuando es Typed es obligatorio proveer <paramref name="typedName"/>.
    /// </summary>
    internal Result RecordSigned(
        DateTime signedAtUtc,
        SignatureCaptureMethod method,
        string? typedName,
        Guid? signatureImageFileId,
        string? clientIp,
        string? userAgent
    )
    {
        if (Status == SignerStatus.Signed)
            return Result.Success();

        // Default para el flujo legacy (staff que firma sin especificar método): si el
        // método es Typed y no vino nombre explícito, se toma del propio signer.
        var effectiveTypedName =
            method == SignatureCaptureMethod.Typed && string.IsNullOrWhiteSpace(typedName) ? FullName.Value : typedName;

        var evidenceValidation = ValidateCaptureEvidence(method, effectiveTypedName, signatureImageFileId);
        if (evidenceValidation.IsFailure)
            return evidenceValidation;

        EnsurePending();
        Status = SignerStatus.Signed;
        SignedAtUtc = signedAtUtc;
        CaptureMethod = method;
        TypedName = method == SignatureCaptureMethod.Typed ? effectiveTypedName?.Trim() : null;
        SignatureImageFileId = method == SignatureCaptureMethod.Typed ? null : signatureImageFileId;
        ClientIp = TruncateIp(clientIp);
        UserAgent = TruncateUserAgent(userAgent);
        return Result.Success();
    }

    private static Result ValidateCaptureEvidence(
        SignatureCaptureMethod method,
        string? typedName,
        Guid? signatureImageFileId
    )
    {
        if (method == SignatureCaptureMethod.Typed && string.IsNullOrWhiteSpace(typedName))
            return Result.Failure(
                new Error("Signature.Signer.TypedNameRequired", "Typed name is required for Typed method.")
            );
        if (method is SignatureCaptureMethod.Drawn or SignatureCaptureMethod.Uploaded)
        {
            if (signatureImageFileId is null || signatureImageFileId == Guid.Empty)
                return Result.Failure(
                    new Error(
                        "Signature.Signer.SignatureImageRequired",
                        "Signature image FileId is required for Drawn/Uploaded method."
                    )
                );
        }
        return Result.Success();
    }

    /// <summary>Marca al firmante como Rejected con motivo opcional.</summary>
    internal Result RecordRejected(DateTime rejectedAtUtc, string? reason, string? clientIp, string? userAgent)
    {
        EnsurePending();
        Status = SignerStatus.Rejected;
        RejectedAtUtc = rejectedAtUtc;
        RejectReason = TruncateReason(reason);
        ClientIp = TruncateIp(clientIp);
        UserAgent = TruncateUserAgent(userAgent);
        return Result.Success();
    }

    /// <summary>
    /// Registra la aceptación del consent form. Idempotente: si ya aceptó, no hace nada.
    /// </summary>
    internal Result RecordConsentAcceptance(DateTime acceptedAtUtc, string? clientIp, string? userAgent)
    {
        if (HasAcceptedConsent)
            return Result.Success();

        EnsurePending();
        HasAcceptedConsent = true;
        ConsentAcceptedAtUtc = acceptedAtUtc;
        ClientIp = TruncateIp(clientIp);
        UserAgent = TruncateUserAgent(userAgent);
        return Result.Success();
    }

    /// <summary>
    /// Marca la primera apertura del enlace por el firmante. Idempotente: solo se
    /// asigna en la primera invocación (para trazabilidad histórica).
    /// </summary>
    internal void RecordFirstView(DateTime viewedAtUtc, string? clientIp, string? userAgent)
    {
        if (FirstViewedAtUtc is not null)
            return;

        FirstViewedAtUtc = viewedAtUtc;
        if (ClientIp is null)
            ClientIp = TruncateIp(clientIp);
        if (UserAgent is null)
            UserAgent = TruncateUserAgent(userAgent);
    }

    // ------------------------------------------------------------------
    // Practitioner PIN — cada regla en su método
    // ------------------------------------------------------------------

    public const int MaxPinAttempts = 5;
    public static readonly TimeSpan PinLockDuration = TimeSpan.FromMinutes(30);

    /// <summary>
    /// <c>true</c> si el firmante está bloqueado por intentos fallidos en el instante
    /// <paramref name="now"/>. Se auto-libera cuando pasa <see cref="PinLockedUntilUtc"/>.
    /// </summary>
    public bool IsPinLockedAt(DateTime now) => PinLockedUntilUtc is not null && PinLockedUntilUtc > now;

    /// <summary>Registra un intento exitoso: quedan sellados el timestamp y se limpian los contadores.</summary>
    internal Result RecordPinVerified(DateTime verifiedAtUtc, string? clientIp, string? userAgent)
    {
        if (IsPinVerified)
            return Result.Success();

        EnsurePending();
        IsPinVerified = true;
        PinVerifiedAtUtc = verifiedAtUtc;
        PinFailedAttempts = 0;
        PinLockedUntilUtc = null;
        ClientIp = TruncateIp(clientIp) ?? ClientIp;
        UserAgent = TruncateUserAgent(userAgent) ?? UserAgent;
        return Result.Success();
    }

    /// <summary>
    /// Incrementa el contador de intentos fallidos. Si alcanza <see cref="MaxPinAttempts"/>
    /// bloquea al firmante por <see cref="PinLockDuration"/>. El bloqueo NO impide la firma
    /// automáticamente — lo controla el aggregate en <c>VerifySignerWithPin</c>.
    /// </summary>
    internal Result RecordPinFailedAttempt(DateTime attemptedAtUtc, string? clientIp, string? userAgent)
    {
        EnsurePending();

        // Si el lock previo ya expiró, reseteamos antes de contar el intento actual.
        if (PinLockedUntilUtc is not null && PinLockedUntilUtc <= attemptedAtUtc)
        {
            PinFailedAttempts = 0;
            PinLockedUntilUtc = null;
        }

        PinFailedAttempts++;
        ClientIp = TruncateIp(clientIp) ?? ClientIp;
        UserAgent = TruncateUserAgent(userAgent) ?? UserAgent;

        if (PinFailedAttempts >= MaxPinAttempts)
            PinLockedUntilUtc = attemptedAtUtc.Add(PinLockDuration);

        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Verification challenges genéricos (SMS/Email/WhatsApp/KBA)
    // ------------------------------------------------------------------

    /// <summary>Retorna el último challenge activo (no consumido, no expirado) del método dado.</summary>
    public SignerVerificationChallenge? CurrentChallengeFor(SignerVerificationMethod method, DateTime now) =>
        _challenges
            .Where(c => c.Method == method && c.IsActiveAt(now))
            .OrderByDescending(c => c.IssuedAtUtc)
            .FirstOrDefault();

    /// <summary>Añade un nuevo challenge y desactiva cualquier anterior activo del mismo método.</summary>
    internal Result AttachChallenge(SignerVerificationChallenge challenge, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (challenge.SignerId != Id)
            return Result.Failure(
                new Error("Signature.Signer.ChallengeOwnership", "Challenge does not belong to this signer.")
            );

        EnsurePending();
        InvalidatePreviousChallengesOf(challenge.Method, now);
        _challenges.Add(challenge);
        return Result.Success();
    }

    /// <summary>Marca el challenge como consumido. El firmante queda verificado en ese método.</summary>
    internal Result ConsumeChallenge(SignerVerificationChallenge challenge, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        EnsurePending();
        challenge.MarkConsumed(now);
        return Result.Success();
    }

    private void InvalidatePreviousChallengesOf(SignerVerificationMethod method, DateTime now)
    {
        foreach (var existing in _challenges)
        {
            if (existing.Method == method && existing.IsActiveAt(now))
                existing.MarkConsumed(now);
        }
    }

    /// <summary>Marca al firmante como Expired cuando la solicitud completa expira.</summary>
    internal Result RecordExpired()
    {
        if (Status is SignerStatus.Signed or SignerStatus.Rejected)
            return Result.Success();

        Status = SignerStatus.Expired;
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Helpers privados
    // ------------------------------------------------------------------

    private void EnsurePending()
    {
        if (Status != SignerStatus.Pending)
            throw new InvalidOperationException($"Signer {Id} is in terminal status {Status} and cannot be modified.");
    }

    private static string? TruncateIp(string? ip) =>
        string.IsNullOrWhiteSpace(ip) ? null
        : ip.Trim() is { Length: > 45 } trimmed ? trimmed[..45]
        : ip.Trim();

    private static string? TruncateUserAgent(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent) ? null
        : userAgent.Trim() is { Length: > 500 } trimmed ? trimmed[..500]
        : userAgent.Trim();

    private static string? TruncateReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null
        : reason.Trim() is { Length: > 2000 } trimmed ? trimmed[..2000]
        : reason.Trim();
}
