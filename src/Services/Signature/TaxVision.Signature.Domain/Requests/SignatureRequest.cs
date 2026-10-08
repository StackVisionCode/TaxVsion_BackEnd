// F2: SignatureRequestStatus.Ready está obsoleto pero la lógica lo tolera para filas históricas
// (ver anotación [Obsolete] en el enum). Silenciamos CS0618 en el archivo entero porque esa
// tolerancia es intencional en cada mutación y en los predicados "pre-envío".
#pragma warning disable CS0618
using System.ComponentModel.DataAnnotations.Schema;
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
public sealed class SignatureRequest : AggregateRoot, IHasOwner
{
    public const int MinTitleLength = 3;
    public const int MaxTitleLength = 300;
    public const int MaxDescriptionLength = 2000;

    /// <summary>Máximo del nombre de categoría (sistema o custom del tenant). Guardado como texto congelado.</summary>
    public const int MaxCategoryLength = 64;
    public const int MinSigners = 1;
    public const int MaxSigners = 50;
    public const int MaxDocuments = 20;

    public const int MinReminderIntervalHours = 1;
    public const int MaxReminderIntervalHours = 720; // 30 días

    /// <summary>Tope de seguridad de reminders por solicitud (anti-runaway). La expiración los corta antes.</summary>
    public const int MaxRemindersPerRequest = 20;

    private readonly List<Signer> _signers = [];
    private readonly List<PreparerField> _preparerFields = [];
    private readonly List<RequestDocument> _documents = [];

    private SignatureRequest() { }

    public Guid CreatedByUserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>Nombre de la categoría (de sistema o custom del tenant) congelado como texto en la solicitud.</summary>
    public string Category { get; private set; } = default!;
    public SignatureRequestStatus Status { get; private set; }

    public Guid? CertificateFileId { get; private set; }

    public bool RequiresSequentialSigning { get; private set; }
    public bool RequiresConsent { get; private set; }
    public bool GenerateCertificate { get; private set; }

    /// <summary>
    /// F7 — entrega del PDF final SELLADO (todas las firmas + PAdES) a los firmantes al completar.
    /// Default <c>false</c>: el preparador decide explícitamente si lo manda. Editable hasta `InProgress`.
    /// </summary>
    public bool SendSealedDocumentToSigners { get; private set; }

    /// <summary>
    /// Certificate of Completion a los firmantes al completar. Default <c>false</c>.
    /// Requiere <see cref="GenerateCertificate"/>. Editable hasta `InProgress`.
    /// </summary>
    public bool SendCertificateToSigners { get; private set; }

    /// <summary>
    /// F7 — copia inmediata al firmar: cada firmante incluido en <see cref="PartialCopyAudience"/>
    /// recibe un PDF con sus firmas estampadas (sin PAdES, con watermark de "en progreso"). Default
    /// <c>false</c>. Independiente del documento sellado final.
    /// </summary>
    public bool SendPartialCopyOnEachSignature { get; private set; }

    /// <summary>
    /// A quién le llega la copia parcial. Siempre no-nulo; cuando el flag está OFF se mantiene
    /// como <c>All</c> para que el aggregate nunca tenga null-check.
    /// </summary>
    public PartialCopyAudience PartialCopyAudience { get; private set; } = PartialCopyAudience.All();

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

    /// <summary>
    /// Snapshot inmutable del FileId de la firma reutilizable elegida para estampar por el preparador.
    /// Se congela al colocar/elegir (no un FK a SignatureProfile) para sobrevivir a renombrar/borrar el
    /// perfil antes del sellado, que ocurre al completarse (posiblemente días después).
    /// </summary>
    public Guid? PreparerSignatureFileId { get; private set; }

    /// <summary>Campos del preparador colocados sobre el documento (su firma se estampa al sellar).</summary>
    public IReadOnlyList<PreparerField> PreparerFields => _preparerFields.AsReadOnly();

    /// <summary>F7 — si <c>false</c> el enlace nunca expira: ExpiresAtUtc=null y los recordatorios no corren.</summary>
    public bool ExpirationEnabled { get; private set; } = true;

    /// <summary>Horas desde el envío. Null ⟺ ExpirationEnabled=false.</summary>
    public int? TokenExpirationHours { get; private set; }

    /// <summary>Null cuando ExpirationEnabled=false o antes de enviar.</summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    /// <summary>Contador monotónico incremental. Invalidar todos los tokens vigentes = incrementar.</summary>
    public int RevocationEpoch { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>F3 — hora UTC a la que el job debe transicionar la solicitud a InProgress.</summary>
    public DateTime? ScheduledSendAtUtc { get; private set; }
    public DateTime? CanceledAtUtc { get; private set; }
    public DateTime? ExpiredAtUtc { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public Guid? RejectedBySignerId { get; private set; }

    /// <summary>Timestamp de la última tanda de reminders emitida por el scheduler.</summary>
    public DateTime? LastReminderSentAtUtc { get; private set; }

    /// <summary>Contador de reminders emitidos — cap útil para no spammear al firmante.</summary>
    public int RemindersSent { get; private set; }

    /// <summary>
    /// Si el scheduler debe recordar automáticamente a los firmantes pendientes de esta solicitud.
    /// Se resuelve al crear (override del preparador o default de tenant). Editable en Draft/Ready.
    /// </summary>
    public bool AutoRemindersEnabled { get; private set; } = true;

    /// <summary>Cada cuántas horas se recuerda mientras haya firmantes pendientes (dinámico).</summary>
    public int ReminderIntervalHours { get; private set; } = MinReminderIntervalHours;

    /// <summary>Legal hold activo — el PurgeScheduler NO purga la solicitud mientras esté en <c>true</c>.</summary>
    public bool LegalHold { get; private set; }

    /// <summary>Motivo textual (subpoena #, ticket legal) del hold. Solo se lee por el staff con permiso RequestRead.</summary>
    public string? LegalHoldReason { get; private set; }

    public Guid? LegalHoldPlacedByUserId { get; private set; }
    public DateTime? LegalHoldPlacedAtUtc { get; private set; }
    public Guid? LegalHoldLiftedByUserId { get; private set; }
    public DateTime? LegalHoldLiftedAtUtc { get; private set; }

    public IReadOnlyList<Signer> Signers => _signers.AsReadOnly();
    public IReadOnlyList<RequestDocument> Documents => _documents.AsReadOnly();

    // Puente de compilación durante la migración F8. No forma parte del modelo EF y se elimina
    // cuando todos los callers de T3-T6 consuman Documents de manera explícita.
    [NotMapped]
    [Obsolete("Use Documents and select an explicit document.")]
    public Guid OriginalFileId => _documents.Count == 1 ? _documents[0].OriginalFileId : Guid.Empty;

    [NotMapped]
    [Obsolete("Use Documents and select an explicit document.")]
    public DocumentHash? DocumentHashPre => _documents.Count == 1 ? _documents[0].DocumentHashPre : null;

    [NotMapped]
    [Obsolete("Use Documents and select an explicit document.")]
    public Guid? SealedFileId => _documents.Count == 1 ? _documents[0].SealedFileId : null;

    [NotMapped]
    [Obsolete("Use Documents and select an explicit document.")]
    public DocumentHash? DocumentHashPost => _documents.Count == 1 ? _documents[0].DocumentHashPost : null;

    // ------------------------------------------------------------------
    // Factory
    // ------------------------------------------------------------------

    public static Result<SignatureRequest> CreateDraft(
        Guid tenantId,
        Guid createdByUserId,
        string title,
        string? description,
        string category,
        int tokenExpirationHours,
        bool requiresSequentialSigning,
        bool requiresConsent,
        bool generateCertificate,
        bool sendSealedDocumentToSigners = false,
        bool sendCertificateToSigners = false,
        bool autoRemindersEnabled = true,
        int reminderIntervalHours = 48,
        bool expirationEnabled = true,
        bool sendPartialCopyOnEachSignature = false,
        PartialCopyAudience? partialCopyAudience = null
    )
    {
        var baseValidation = ValidateFactoryInputs(tenantId, createdByUserId, title, description, tokenExpirationHours);
        if (baseValidation.IsFailure)
            return Result.Failure<SignatureRequest>(baseValidation.Error);

        var categoryCheck = ValidateCategory(category);
        if (categoryCheck.IsFailure)
            return Result.Failure<SignatureRequest>(categoryCheck.Error);

        // F7 — audiencia obligatoria si el flag está ON; con el flag OFF ignoramos lo que venga.
        if (sendPartialCopyOnEachSignature && partialCopyAudience is null)
            return Result.Failure<SignatureRequest>(
                new Error(
                    "Signature.Request.PartialCopyRequiresAudience",
                    "Partial copy delivery requires an audience."
                )
            );

        var now = DateTime.UtcNow;
        var request = new SignatureRequest
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = createdByUserId,
            Title = title.Trim(),
            Description = NormalizeDescription(description),
            Category = category.Trim(),
            Status = SignatureRequestStatus.Draft,
            ExpirationEnabled = expirationEnabled,
            TokenExpirationHours = expirationEnabled ? tokenExpirationHours : null,
            // Provisional: el borrador no expira; Send lo recalcula desde la fecha de envío (si aplica).
            ExpiresAtUtc = expirationEnabled ? now.AddHours(tokenExpirationHours) : null,
            RequiresSequentialSigning = requiresSequentialSigning,
            RequiresConsent = requiresConsent,
            GenerateCertificate = generateCertificate,
            SendSealedDocumentToSigners = sendSealedDocumentToSigners,
            SendCertificateToSigners = sendCertificateToSigners && generateCertificate,
            SendPartialCopyOnEachSignature = sendPartialCopyOnEachSignature,
            PartialCopyAudience = sendPartialCopyOnEachSignature ? partialCopyAudience! : PartialCopyAudience.All(),
            AutoRemindersEnabled = autoRemindersEnabled,
            ReminderIntervalHours = Math.Clamp(
                reminderIntervalHours,
                MinReminderIntervalHours,
                MaxReminderIntervalHours
            ),
            RevocationEpoch = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        request.SetTenant(tenantId);
        return Result.Success(request);
    }

    [Obsolete("Create the request first and add documents explicitly.")]
    public static Result<SignatureRequest> CreateDraft(
        Guid tenantId,
        Guid createdByUserId,
        string title,
        string? description,
        string category,
        Guid originalFileId,
        int tokenExpirationHours,
        bool requiresSequentialSigning,
        bool requiresConsent,
        bool generateCertificate,
        bool sendSealedDocumentToSigners = false,
        bool sendCertificateToSigners = false,
        bool autoRemindersEnabled = true,
        int reminderIntervalHours = 48,
        bool expirationEnabled = true,
        bool sendPartialCopyOnEachSignature = false,
        PartialCopyAudience? partialCopyAudience = null
    )
    {
        if (originalFileId == Guid.Empty)
            return Result.Failure<SignatureRequest>(
                new Error("Signature.Request.OriginalFile", "OriginalFileId is required.")
            );

        var requestResult = CreateDraft(
            tenantId,
            createdByUserId,
            title,
            description,
            category,
            tokenExpirationHours,
            requiresSequentialSigning,
            requiresConsent,
            generateCertificate,
            sendSealedDocumentToSigners,
            sendCertificateToSigners,
            autoRemindersEnabled,
            reminderIntervalHours,
            expirationEnabled,
            sendPartialCopyOnEachSignature,
            partialCopyAudience
        );
        if (requestResult.IsFailure)
            return requestResult;

        var documentResult = requestResult.Value.AddDocument(originalFileId, title);
        return documentResult.IsFailure ? Result.Failure<SignatureRequest>(documentResult.Error) : requestResult;
    }

    // ------------------------------------------------------------------
    // Documents
    // ------------------------------------------------------------------

    public Result<RequestDocument> AddDocument(Guid fileId, string title, string? note = null)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return Result.Failure<RequestDocument>(editable.Error);
        if (_documents.Count >= MaxDocuments)
            return Result.Failure<RequestDocument>(
                new Error("Signature.Request.TooManyDocuments", $"Document count cannot exceed {MaxDocuments}.")
            );
        if (_documents.Any(document => document.OriginalFileId == fileId))
            return Result.Failure<RequestDocument>(
                new Error("Signature.Request.DuplicateDocument", "This file is already part of the request.")
            );

        var result = RequestDocument.Create(TenantId, Id, NextDocumentOrder(), title, fileId, note);
        if (result.IsFailure)
            return result;

        _documents.Add(result.Value);
        Touch();
        return result;
    }

    public Result RemoveDocument(Guid documentId)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

        var document = FindDocumentOrNull(documentId);
        if (document is null)
            return Result.Failure(
                new Error("Signature.Request.DocumentMissing", "Document not found in this request.")
            );

        foreach (var signer in _signers)
        {
            signer.RemoveFieldsForDocument(documentId);
            signer.RemoveDocumentView(documentId);
            signer.RemoveDocumentCompletion(documentId);
        }
        _preparerFields.RemoveAll(field => field.DocumentId == documentId);
        _documents.Remove(document);
        NormalizeDocumentOrder();
        Touch();
        return Result.Success();
    }

    public Result ReorderDocuments(IReadOnlyList<Guid> orderedDocumentIds)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;
        ArgumentNullException.ThrowIfNull(orderedDocumentIds);
        if (orderedDocumentIds.Count != _documents.Count || orderedDocumentIds.Distinct().Count() != _documents.Count)
            return Result.Failure(
                new Error(
                    "Signature.Request.DocumentReorderMismatch",
                    "The provided order does not match the current document collection."
                )
            );

        var lookup = _documents.ToDictionary(document => document.Id);
        var reordered = new List<RequestDocument>(_documents.Count);
        for (var index = 0; index < orderedDocumentIds.Count; index++)
        {
            if (!lookup.TryGetValue(orderedDocumentIds[index], out var document))
                return Result.Failure(
                    new Error("Signature.Request.DocumentReorderUnknown", "Unknown document id in the requested order.")
                );

            var result = document.Reorder(index + 1);
            if (result.IsFailure)
                return result;
            reordered.Add(document);
        }

        _documents.Clear();
        _documents.AddRange(reordered);
        Touch();
        return Result.Success();
    }

    public Result ReplaceDocumentFile(Guid documentId, Guid newFileId)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;
        var document = FindDocumentOrNull(documentId);
        if (document is null)
            return Result.Failure(
                new Error("Signature.Request.DocumentMissing", "Document not found in this request.")
            );
        if (_documents.Any(candidate => candidate.Id != documentId && candidate.OriginalFileId == newFileId))
            return Result.Failure(
                new Error("Signature.Request.DuplicateDocument", "This file is already part of the request.")
            );

        var result = document.ReplaceOriginalFile(newFileId);
        if (result.IsSuccess)
            Touch();
        return result;
    }

    public Result RenameDocument(Guid documentId, string title)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

        var document = FindDocumentOrNull(documentId);
        if (document is null)
            return Result.Failure(
                new Error("Signature.Request.DocumentMissing", "Document not found in this request.")
            );

        var result = document.Rename(title);
        if (result.IsSuccess)
            Touch();
        return result;
    }

    public Result AttachDocumentHash(Guid documentId, DocumentHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (Status != SignatureRequestStatus.Draft)
            return Result.Failure(
                new Error("Signature.Request.NotDraft", "Only a Draft request can attach a document hash.")
            );

        var document = FindDocumentOrNull(documentId);
        if (document is null)
            return Result.Failure(
                new Error("Signature.Request.DocumentMissing", "Document not found in this request.")
            );

        var result = document.AttachHashPre(hash);
        if (result.IsSuccess)
            Touch();
        return result;
    }

    public Result MarkDocumentSealed(Guid documentId, Guid sealedFileId, DocumentHash hashPost, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(hashPost);
        var document = FindDocumentOrNull(documentId);
        if (document is null)
            return Result.Failure(
                new Error("Signature.Request.DocumentMissing", "Document not found in this request.")
            );

        if (!IsDocumentReadyForSealing(documentId))
            return Result.Failure(
                new Error(
                    "Signature.Request.DocumentNotReady",
                    "A document can only be sealed after all of its participating signers complete it."
                )
            );

        var result = document.MarkSealed(sealedFileId, hashPost, nowUtc);
        if (result.IsSuccess)
            Touch();
        return result;
    }

    public bool AllDocumentsSealed() =>
        _documents.Count > 0
        && _documents.All(document =>
            document.SealedFileId is not null
            && document.DocumentHashPost is not null
            && document.SealedAtUtc is not null
        );

    // ------------------------------------------------------------------
    // Edición de metadata del borrador (solo Draft/Ready)
    // ------------------------------------------------------------------

    /// <summary>
    /// Edita la metadata del borrador: título, descripción, categoría y horas de expiración del token.
    /// Solo en Draft/Ready. Las horas actualizan la expiración provisional; Send la recalcula desde el envío.
    /// </summary>
    public Result UpdateMetadata(string title, string? description, string category, int tokenExpirationHours)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

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

        if (tokenExpirationHours is < 1 or > 720)
            return Result.Failure(
                new Error("Signature.Request.TokenExpiration", "Token expiration must be between 1 and 720 hours.")
            );

        var categoryCheck = ValidateCategory(category);
        if (categoryCheck.IsFailure)
            return categoryCheck;

        Title = trimmedTitle;
        Description = NormalizeDescription(description);
        Category = category.Trim();
        // F7 — solo tiene sentido reajustar las horas si la expiración está activa.
        if (ExpirationEnabled)
        {
            TokenExpirationHours = tokenExpirationHours;
            ExpiresAtUtc = DateTime.UtcNow.AddHours(tokenExpirationHours);
        }
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Entrega (P2) — qué se envía a los firmantes al completar
    // ------------------------------------------------------------------

    /// <summary>Activa/desactiva la entrega del PDF sellado final a los firmantes. Solo en Draft/Ready.</summary>
    public Result SetSealedDocumentDelivery(bool enabled)
    {
        if (Status is not (SignatureRequestStatus.Draft or SignatureRequestStatus.Ready))
            return Result.Failure(
                new Error("Signature.Request.NotEditable", "Delivery settings can only change while Draft or Ready.")
            );

        SendSealedDocumentToSigners = enabled;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// F7 — activa/desactiva la copia inmediata al firmar y fija su audiencia. OFF deja la
    /// audiencia como All (sin null-check aguas abajo). Editable mientras no esté Completed/Canceled.
    /// </summary>
    public Result SetSendPartialCopy(bool enabled, PartialCopyAudience? audience)
    {
        if (
            Status
            is SignatureRequestStatus.Completed
                or SignatureRequestStatus.Canceled
                or SignatureRequestStatus.Rejected
                or SignatureRequestStatus.Expired
        )
            return Result.Failure(
                new Error(
                    "Signature.Request.NotEditable",
                    "Partial copy settings can only change while the request is not finalized."
                )
            );

        if (enabled)
        {
            if (audience is null)
                return Result.Failure(
                    new Error(
                        "Signature.Request.PartialCopyRequiresAudience",
                        "Partial copy delivery requires an audience."
                    )
                );

            if (audience.Kind == PartialCopyAudienceKind.Specific)
            {
                var known = _signers.Select(s => s.Id).ToHashSet();
                var unknown = audience.SpecificSignerIds.Where(id => !known.Contains(id)).ToList();
                if (unknown.Count > 0)
                    return Result.Failure(
                        new Error(
                            "Signature.Request.PartialCopyAudienceInvalid",
                            $"Audience contains {unknown.Count} signer id(s) that don't belong to this request."
                        )
                    );
            }

            SendPartialCopyOnEachSignature = true;
            PartialCopyAudience = audience;
        }
        else
        {
            SendPartialCopyOnEachSignature = false;
            PartialCopyAudience = PartialCopyAudience.All();
        }

        Touch();
        return Result.Success();
    }

    /// <summary>
    /// F7 — activa la expiración del enlace con las horas dadas. Si la request ya está InProgress,
    /// recalcula ExpiresAtUtc desde SentAtUtc. Rechaza horas fuera de rango.
    /// </summary>
    public Result EnableExpiration(int tokenExpirationHours)
    {
        if (
            Status
            is SignatureRequestStatus.Completed
                or SignatureRequestStatus.Canceled
                or SignatureRequestStatus.Rejected
                or SignatureRequestStatus.Expired
        )
            return Result.Failure(
                new Error("Signature.Request.NotEditable", "Expiration cannot change on a finalized request.")
            );

        if (tokenExpirationHours is < 1 or > 720)
            return Result.Failure(
                new Error("Signature.Request.TokenExpiration", "Token expiration must be between 1 and 720 hours.")
            );

        ExpirationEnabled = true;
        TokenExpirationHours = tokenExpirationHours;
        // Si ya se envió, cuenta desde SentAtUtc; si sigue en borrador, desde ahora (provisional).
        var anchor = SentAtUtc ?? DateTime.UtcNow;
        ExpiresAtUtc = anchor.AddHours(tokenExpirationHours);
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// F7 — desactiva la expiración: el enlace nunca vence. ExpiresAtUtc=null, horas=null,
    /// y los recordatorios automáticos quedan sin reloj (ReminderPolicy los saltará).
    /// </summary>
    public Result DisableExpiration()
    {
        if (
            Status
            is SignatureRequestStatus.Completed
                or SignatureRequestStatus.Canceled
                or SignatureRequestStatus.Rejected
                or SignatureRequestStatus.Expired
        )
            return Result.Failure(
                new Error("Signature.Request.NotEditable", "Expiration cannot change on a finalized request.")
            );

        ExpirationEnabled = false;
        TokenExpirationHours = null;
        ExpiresAtUtc = null;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Activa/desactiva la entrega del certificado a los firmantes. Requiere que la request genere
    /// certificado (<see cref="GenerateCertificate"/>), pues sin él no hay nada que entregar. Solo Draft/Ready.
    /// </summary>
    public Result SetCertificateDelivery(bool enabled)
    {
        if (Status is not (SignatureRequestStatus.Draft or SignatureRequestStatus.Ready))
            return Result.Failure(
                new Error("Signature.Request.NotEditable", "Delivery settings can only change while Draft or Ready.")
            );

        if (enabled && !GenerateCertificate)
            return Result.Failure(
                new Error(
                    "Signature.Request.CertificateNotGenerated",
                    "Enable certificate generation before delivering it to signers."
                )
            );

        SendCertificateToSigners = enabled;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Configura los recordatorios automáticos a firmantes: on/off e intervalo (horas). Solo Draft/Ready.
    /// El intervalo se acota al rango válido.
    /// </summary>
    public Result SetReminderPolicy(bool enabled, int intervalHours)
    {
        if (Status is not (SignatureRequestStatus.Draft or SignatureRequestStatus.Ready))
            return Result.Failure(
                new Error("Signature.Request.NotEditable", "Reminder settings can only change while Draft or Ready.")
            );

        if (intervalHours is < MinReminderIntervalHours or > MaxReminderIntervalHours)
            return Result.Failure(
                new Error(
                    "Signature.Request.ReminderInterval",
                    $"Reminder interval must be between {MinReminderIntervalHours} and {MaxReminderIntervalHours} hours."
                )
            );

        AutoRemindersEnabled = enabled;
        ReminderIntervalHours = intervalHours;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// <c>true</c> si a esta solicitud le toca un recordatorio en <paramref name="now"/>: está InProgress,
    /// tiene reminders activos, no expiró, no superó el cap, y pasó el intervalo desde el último envío
    /// (o desde el envío inicial si aún no hubo ninguno). La regla vive en el dominio para poder testearla;
    /// la query del repositorio la refleja en SQL para no cargar toda la tabla.
    /// </summary>
    public bool IsReminderDue(DateTime now)
    {
        if (Status != SignatureRequestStatus.InProgress || !AutoRemindersEnabled)
            return false;
        // F7 — sin expiración no hay deadline que recordar.
        if (!ExpirationEnabled || ExpiresAtUtc is not DateTime expiresAt)
            return false;
        if (now >= expiresAt || RemindersSent >= MaxRemindersPerRequest)
            return false;

        var baseline = LastReminderSentAtUtc ?? SentAtUtc;
        if (baseline is null)
            return false;

        return baseline.Value.AddHours(ReminderIntervalHours) <= now;
    }

    // ------------------------------------------------------------------
    // Signers — cada operación con SU regla
    // ------------------------------------------------------------------

    public Result<Signer> AddSigner(
        SignerEmail email,
        SignerFullName fullName,
        Guid? mappedCustomerId,
        SignerPhoneNumber? phoneNumber = null,
        string? language = null,
        SignerVerificationMethod? requiredVerificationMethod = null
    )
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return Result.Failure<Signer>(editable.Error);

        if (_signers.Count >= MaxSigners)
            return Result.Failure<Signer>(
                new Error("Signature.Request.TooManySigners", $"Signer count cannot exceed {MaxSigners}.")
            );

        if (EmailAlreadyPresent(email))
            return Result.Failure<Signer>(
                new Error("Signature.Request.DuplicateSignerEmail", "This email is already registered as a signer.")
            );

        var order = NextOrder();
        var signerResult = Signer.Create(
            Id,
            email,
            fullName,
            mappedCustomerId,
            order,
            phoneNumber,
            language,
            requiredVerificationMethod
        );
        if (signerResult.IsFailure)
            return signerResult;

        _signers.Add(signerResult.Value);
        Touch();
        return signerResult;
    }

    /// <summary>Actualiza el teléfono del firmante en <c>Draft</c>/<c>Ready</c>. Solo permitido antes de Send.</summary>
    public Result SetSignerPhoneNumber(Guid signerId, SignerPhoneNumber? phoneNumber)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;
        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        var result = signer.SetPhoneNumber(phoneNumber);
        if (result.IsFailure)
            return result;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Fija (o quita con <c>null</c>) el método de verificación de identidad que el firmante
    /// debe completar antes de firmar. Solo permitido en <c>Draft</c>/<c>Ready</c> (antes de Send).
    /// </summary>
    public Result SetSignerRequiredVerificationMethod(Guid signerId, SignerVerificationMethod? method)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;
        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        var result = signer.SetRequiredVerificationMethod(method);
        if (result.IsFailure)
            return result;
        Touch();
        return Result.Success();
    }

    public Result RemoveSigner(Guid signerId)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

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

        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

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
        Guid documentId,
        SignatureFieldKind kind,
        FieldPosition position,
        string? label,
        bool isRequired
    )
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return Result.Failure<SignatureField>(editable.Error);

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure<SignatureField>(
                new Error("Signature.Request.SignerMissing", "Cannot place a field on an unknown signer.")
            );

        if (FindDocumentOrNull(documentId) is null)
            return Result.Failure<SignatureField>(
                new Error("Signature.Request.DocumentMissing", "Cannot place a field on an unknown document.")
            );

        var fieldResult = SignatureField.Create(Id, signerId, documentId, kind, position, label, isRequired);
        if (fieldResult.IsFailure)
            return fieldResult;

        var addResult = signer.AddField(fieldResult.Value);
        if (addResult.IsFailure)
            return Result.Failure<SignatureField>(addResult.Error);

        Touch();
        return fieldResult;
    }

    [Obsolete("Pass DocumentId explicitly for multi-document requests.")]
    public Result<SignatureField> PlaceField(
        Guid signerId,
        SignatureFieldKind kind,
        FieldPosition position,
        string? label,
        bool isRequired
    ) =>
        _documents.Count == 1
            ? PlaceField(signerId, _documents[0].Id, kind, position, label, isRequired)
            : Result.Failure<SignatureField>(
                new Error("Signature.Request.DocumentRequired", "DocumentId is required for multi-document requests.")
            );

    public Result RemoveField(Guid signerId, Guid fieldId)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

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
    // Preparer fields — placement del canal paralelo del preparador
    // ------------------------------------------------------------------

    /// <summary>Coloca un campo del preparador (solo Draft/Ready). Su firma se estampa al sellar.</summary>
    public Result<PreparerField> PlacePreparerField(
        Guid documentId,
        SignatureFieldKind kind,
        FieldPosition position,
        string? label
    )
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return Result.Failure<PreparerField>(editable.Error);

        if (FindDocumentOrNull(documentId) is null)
            return Result.Failure<PreparerField>(
                new Error("Signature.Request.DocumentMissing", "Cannot place a preparer field on an unknown document.")
            );

        var fieldResult = PreparerField.Create(Id, documentId, kind, position, label);
        if (fieldResult.IsFailure)
            return fieldResult;

        _preparerFields.Add(fieldResult.Value);
        Touch();
        return fieldResult;
    }

    [Obsolete("Pass DocumentId explicitly for multi-document requests.")]
    public Result<PreparerField> PlacePreparerField(SignatureFieldKind kind, FieldPosition position, string? label) =>
        _documents.Count == 1
            ? PlacePreparerField(_documents[0].Id, kind, position, label)
            : Result.Failure<PreparerField>(
                new Error("Signature.Request.DocumentRequired", "DocumentId is required for multi-document requests.")
            );

    public Result RemovePreparerField(Guid fieldId)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

        var field = _preparerFields.Find(f => f.Id == fieldId);
        if (field is null)
            return Result.Failure(
                new Error("Signature.PreparerField.NotFound", "The preparer field does not exist in this request.")
            );

        _preparerFields.Remove(field);
        Touch();
        return Result.Success();
    }

    /// <summary>Congela qué firma reutilizable se estampará por el preparador (solo Draft/Ready).</summary>
    public Result SetPreparerSignature(Guid signatureFileId)
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

        if (signatureFileId == Guid.Empty)
            return Result.Failure(new Error("Signature.PreparerField.File", "A signature file is required."));

        PreparerSignatureFileId = signatureFileId;
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Progresión de estado
    // ------------------------------------------------------------------

    [Obsolete("Use AttachDocumentHash with an explicit DocumentId.")]
    public Result AttachOriginalHash(DocumentHash originalHash) =>
        _documents.Count == 1
            ? AttachDocumentHash(_documents[0].Id, originalHash)
            : Result.Failure(
                new Error("Signature.Request.DocumentRequired", "DocumentId is required for multi-document requests.")
            );

    /// <summary>Draft con documento, firmantes y al menos un campo de firma. Vive derivado del estado.</summary>
    public bool IsReadyToSend =>
        Status == SignatureRequestStatus.Draft && ValidateDocumentsForSend().IsSuccess && _signers.Count >= MinSigners;

    /// <summary>Transiciona Draft/Scheduled → InProgress validando documento, firmantes y campos.</summary>
    public Result Send(DateTime sentAtUtc)
    {
        if (
            Status
            is not (SignatureRequestStatus.Draft or SignatureRequestStatus.Ready or SignatureRequestStatus.Scheduled)
        )
            return Result.Failure(new Error("Signature.Request.NotEditable", "Only an editable request can be sent."));

        if (_signers.Count < MinSigners)
            return Result.Failure(
                new Error("Signature.Request.NoSigners", "The request must have at least one signer.")
            );

        var documentValidation = ValidateDocumentsForSend();
        if (documentValidation.IsFailure)
            return documentValidation;

        Status = SignatureRequestStatus.InProgress;
        SentAtUtc = sentAtUtc;
        // Al enviar efectivamente la solicitud ya no "espera" por un reloj futuro: se limpia la fecha.
        ScheduledSendAtUtc = null;
        // F7 — solo computamos vencimiento si la expiración está activa.
        ExpiresAtUtc = ExpirationEnabled && TokenExpirationHours is int hours ? sentAtUtc.AddHours(hours) : null;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// F3 — Programa el envío a una fecha/hora futura. Exige las mismas precondiciones que
    /// <see cref="Send"/>: hash + firmantes + ≥1 campo de firma. Timezone: la UI pasa UTC.
    /// </summary>
    public Result ScheduleSend(DateTime scheduledSendAtUtc, DateTime nowUtc)
    {
        // Draft (actual), Ready (histórico pre-F2, mismo significado editorial) y Scheduled
        // (re-programar). Cualquier otro estado no admite programación.
        if (
            Status
            is not (SignatureRequestStatus.Draft or SignatureRequestStatus.Ready or SignatureRequestStatus.Scheduled)
        )
            return Result.Failure(
                new Error(
                    "Signature.Request.NotSchedulable",
                    $"Only a draft or scheduled request can be scheduled (current status: {Status})."
                )
            );

        if (scheduledSendAtUtc <= nowUtc)
            return Result.Failure(
                new Error("Signature.Request.ScheduleInPast", "The scheduled time must be in the future.")
            );

        if (_signers.Count < MinSigners)
            return Result.Failure(
                new Error("Signature.Request.NoSigners", "The request must have at least one signer.")
            );

        var documentValidation = ValidateDocumentsForSend();
        if (documentValidation.IsFailure)
            return documentValidation;

        Status = SignatureRequestStatus.Scheduled;
        ScheduledSendAtUtc = scheduledSendAtUtc;
        Touch();
        return Result.Success();
    }

    /// <summary>F3 — Cancela la programación de envío. Vuelve a Draft explícitamente.</summary>
    public Result CancelSchedule()
    {
        if (Status != SignatureRequestStatus.Scheduled)
            return Result.Failure(
                new Error(
                    "Signature.Request.NotScheduled",
                    $"Only a scheduled request can be unscheduled (current status: {Status})."
                )
            );

        Status = SignatureRequestStatus.Draft;
        ScheduledSendAtUtc = null;
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
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

        Preparer = preparer;
        PreparerSignedByUserId = null;
        PreparerSignedAtUtc = null;
        Touch();
        return Result.Success();
    }

    /// <summary>Quita el preparer asignado. Sólo permitido en <c>Draft</c> o <c>Ready</c>.</summary>
    public Result ClearPreparer()
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

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
    public Result MarkPreparerSigned(
        Guid preparerUserIdFromCaller,
        DateTime signedAtUtc,
        string? clientIp,
        string? userAgent
    )
    {
        if (preparerUserIdFromCaller == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.PreparerUser", "PreparerUserId is required."));

        if (Preparer is null)
            return Result.Failure(
                new Error("Signature.Request.NoPreparer", "This request does not have a preparer assigned.")
            );

        if (Status is not (SignatureRequestStatus.InProgress or SignatureRequestStatus.Completed))
            return Result.Failure(
                new Error("Signature.Request.PreparerStatus", "Preparer can sign only after the request is InProgress.")
            );

        // A1 — firma el preparer, no cualquiera que tenga el permiso. El PTIN/EFIN identifica a un
        // profesional concreto ante el IRS: si un empleado firma con el de un colega, el PDF sellado sale
        // con una credencial ajena. Las solicitudes anteriores a esta fase no tienen UserId y conservan el
        // comportamiento de antes (§R.7: ningún chequeo nuevo rompe lo que hoy funciona); el override de
        // signature.request.manage vive una capa más arriba, en el controller.
        if (Preparer.UserId is { } preparerUserId && preparerUserId != preparerUserIdFromCaller)
            return Result.Failure(
                new Error(
                    "Signature.Request.PreparerNotSelf",
                    "Only the preparer assigned to this request can sign as preparer."
                )
            );

        if (IsPreparerSigned && PreparerSignedByUserId == preparerUserIdFromCaller)
            return Result.Success();

        if (IsPreparerSigned)
            return Result.Failure(
                new Error("Signature.Request.PreparerAlreadySigned", "Preparer signature already recorded.")
            );

        PreparerSignedByUserId = preparerUserIdFromCaller;
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

        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

        PractitionerPinHash = pinHash;
        PractitionerPinSetByUserId = setByUserId;
        PractitionerPinSetAtUtc = setAtUtc;
        Touch();
        return Result.Success();
    }

    /// <summary>Quita el requerimiento de PIN. Sólo permitido en <c>Draft</c> o <c>Ready</c>.</summary>
    public Result ClearPractitionerPin()
    {
        var editable = EnsureCanBeEdited();
        if (editable.IsFailure)
            return editable;

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
    /// F5 — Marca la primera vez que el firmante vio el DOCUMENTO. Semántica distinta a
    /// <see cref="RecordSignerFirstView"/>: ésta se dispara al servir los bytes del PDF.
    /// Idempotente. Permitida en no-terminal.
    /// </summary>
    public Result RecordSignerDocumentFirstView(
        Guid signerId,
        Guid documentId,
        DateTime viewedAtUtc,
        string? clientIp,
        string? userAgent
    )
    {
        if (IsTerminal())
            return Result.Failure(
                new Error("Signature.Request.Terminal", "Cannot record document view on a terminal request.")
            );

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        if (FindDocumentOrNull(documentId) is null)
            return Result.Failure(
                new Error("Signature.Request.DocumentMissing", "Document not found in this request.")
            );

        var recorded = signer.RecordDocumentFirstView(documentId, viewedAtUtc, clientIp, userAgent);
        if (recorded.IsFailure)
            return recorded;

        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Ancla los valores que el firmante escribió en sus campos de texto (P4), justo antes de
    /// firmar. Delega en el firmante la validación (propiedad del campo, tipo <c>Text</c>,
    /// requeridos completos). Idempotente: reemplaza cualquier captura previa del mismo firmante.
    /// </summary>
    public Result CaptureSignerFieldValues(
        Guid signerId,
        IReadOnlyList<Guid> documentIds,
        IReadOnlyList<SignerFieldValueInput> values,
        DateTime capturedAtUtc
    )
    {
        if (Status != SignatureRequestStatus.InProgress)
            return Result.Failure(
                new Error("Signature.Request.NotInProgress", "Only an InProgress request can capture field values.")
            );

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        var result = signer.CaptureFieldValues(documentIds, values, capturedAtUtc);
        if (result.IsFailure)
            return result;

        Touch();
        return Result.Success();
    }

    [Obsolete("Pass the selected DocumentIds explicitly for multi-document requests.")]
    public Result CaptureSignerFieldValues(
        Guid signerId,
        IReadOnlyList<SignerFieldValueInput> values,
        DateTime capturedAtUtc
    )
    {
        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        return CaptureSignerFieldValues(
            signerId,
            signer.Fields.Select(field => field.DocumentId).Distinct().ToList(),
            values,
            capturedAtUtc
        );
    }

    public bool IsDocumentReadyForSealing(Guid documentId)
    {
        if (Status is not (SignatureRequestStatus.InProgress or SignatureRequestStatus.Completed))
            return false;

        if (FindDocumentOrNull(documentId) is null)
            return false;

        var participants = _signers
            .Where(signer => signer.Fields.Any(field => field.DocumentId == documentId))
            .ToList();
        return participants.Count > 0
            && participants.All(signer =>
                signer.DocumentCompletions.Any(completion => completion.DocumentId == documentId)
            );
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
        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        var completed = MarkSignerDocumentsCompleted(
            signerId,
            signer.Fields.Select(field => field.DocumentId).Distinct().ToList(),
            signedAtUtc,
            captureMethod,
            typedName,
            signatureImageFileId,
            clientIp,
            userAgent
        );
        return completed.IsSuccess ? Result.Success() : Result.Failure(completed.Error);
    }

    /// <summary>Completes only selected documents and keeps the signer active until all are complete.</summary>
    public Result<IReadOnlyList<Guid>> MarkSignerDocumentsCompleted(
        Guid signerId,
        IReadOnlyList<Guid> documentIds,
        DateTime signedAtUtc,
        SignatureCaptureMethod captureMethod,
        string? typedName,
        Guid? signatureImageFileId,
        string? clientIp,
        string? userAgent
    )
    {
        if (Status != SignatureRequestStatus.InProgress)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error("Signature.Request.NotInProgress", "Only an InProgress request can accept signatures.")
            );

        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error("Signature.Request.SignerMissing", "Signer not found in this request.")
            );

        if (RequiresConsent && !signer.HasAcceptedConsent)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error("Signature.Request.ConsentRequired", "Signer must accept the consent before signing.")
            );

        if (RequiresPractitionerPin && !signer.IsPinVerified)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error(
                    "Signature.Request.PinVerificationRequired",
                    "Signer must verify the practitioner PIN before signing."
                )
            );

        // OTP por firmante: espejo del gate del PIN, pero a nivel de cada firmante. Si tiene
        // un método requerido y no completó su challenge, no puede firmar.
        if (signer.RequiredVerificationMethod is { } requiredMethod && !signer.HasCompletedVerification(requiredMethod))
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error(
                    "Signature.Request.VerificationRequired",
                    "Signer must complete identity verification before signing."
                )
            );

        if (RequiresSequentialSigning && !IsSignerNextInSequence(signer))
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error(
                    "Signature.Request.NotYourTurn",
                    "This request is sequential and it is not this signer's turn yet."
                )
            );

        var participatingDocumentIds = signer.Fields.Select(field => field.DocumentId).Distinct().ToList();
        var recordResult = signer.CompleteDocuments(
            documentIds,
            participatingDocumentIds,
            signedAtUtc,
            captureMethod,
            typedName,
            signatureImageFileId,
            clientIp,
            userAgent
        );
        if (recordResult.IsFailure)
            return Result.Failure<IReadOnlyList<Guid>>(recordResult.Error);

        foreach (var documentId in recordResult.Value)
            AddDomainEvent(new SignerCompletedDocument(signer.Id, documentId, signedAtUtc));

        // F7 — engancha la copia parcial una vez. El consumer la lee después via query y la emite.
        if (SendPartialCopyOnEachSignature && PartialCopyAudience.Includes(signer.Id))
            signer.MarkDocumentPartialCopiesRequested(recordResult.Value, signedAtUtc);

        if (AllSignersHaveSigned())
            TransitionToCompleted(signedAtUtc);
        else
            Touch();

        return Result.Success<IReadOnlyList<Guid>>(recordResult.Value);
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

    /// <summary>
    /// Borrado permanente: sólo un borrador sin enviar (Draft o Ready). Una vez enviada la
    /// solicitud se cancela, no se borra — hay firmantes, enlaces y auditoría de por medio.
    /// </summary>
    public Result EnsureCanBeDeleted()
    {
        if (Status is SignatureRequestStatus.Draft or SignatureRequestStatus.Ready)
            return Result.Success();

        return Result.Failure(
            new Error(
                "Signature.Request.NotDeletable",
                "Only an unsent draft can be deleted. Sent, completed, canceled or expired requests are kept for your records."
            )
        );
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

    /// <summary>F7 — el consumer registra que la copia parcial de un signer fue subida a CloudStorage.</summary>
    public Result RecordPartialCopySent(Guid signerId, Guid fileId, DateTime sentAtUtc)
    {
        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        signer.MarkPartialCopySent(fileId, sentAtUtc);
        Touch();
        return Result.Success();
    }

    public Result RecordDocumentPartialCopySent(Guid signerId, Guid documentId, Guid fileId, DateTime sentAtUtc)
    {
        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        signer.MarkDocumentPartialCopySent(documentId, fileId, sentAtUtc);
        Touch();
        return Result.Success();
    }

    /// <summary>F7 — el consumer registra el motivo de fallo al entregar la copia parcial.</summary>
    public Result RecordPartialCopyFailed(Guid signerId, string reason)
    {
        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        signer.MarkPartialCopyFailed(reason);
        Touch();
        return Result.Success();
    }

    public Result RecordDocumentPartialCopyFailed(Guid signerId, Guid documentId, string reason)
    {
        var signer = FindSignerOrNull(signerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found in this request."));

        signer.MarkDocumentPartialCopyFailed(documentId, reason);
        Touch();
        return Result.Success();
    }

    public Result MarkExpired(DateTime expiredAtUtc)
    {
        if (IsTerminal())
            return Result.Failure(new Error("Signature.Request.Terminal", "Cannot expire a terminal request."));

        // F7 — una request sin expiración no debería expirar nunca. Si llega aquí, es un bug del caller.
        if (!ExpirationEnabled)
            return Result.Failure(
                new Error("Signature.Request.ExpirationDisabled", "This request has no expiration; cannot expire.")
            );

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

        // F7 — sin expiración no hay reloj que mover.
        if (!ExpirationEnabled || ExpiresAtUtc is not DateTime currentExpiresAt)
            return Result.Failure(
                new Error("Signature.Request.ExpirationDisabled", "This request has no expiration; nothing to extend.")
            );

        if (additionalHours is < 1 or > 720)
            return Result.Failure(
                new Error("Signature.Request.ExtendRange", "Additional hours must be between 1 and 720.")
            );

        ExpiresAtUtc = currentExpiresAt.AddHours(additionalHours);
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
    [Obsolete("Use MarkDocumentSealed with an explicit DocumentId, then RecordCertificate.")]
    public Result MarkSealed(Guid sealedFileId, DocumentHash sealedHash, Guid? certificateFileId)
    {
        if (_documents.Count != 1)
            return Result.Failure(
                new Error("Signature.Request.DocumentRequired", "DocumentId is required for multi-document requests.")
            );

        var sealResult = MarkDocumentSealed(_documents[0].Id, sealedFileId, sealedHash, DateTime.UtcNow);
        if (sealResult.IsFailure || certificateFileId is null)
            return sealResult;
        return RecordCertificate(certificateFileId.Value);
    }

    [Obsolete("Use MarkDocumentSealed with an explicit DocumentId.")]
    public Result RecordSealedDocument(Guid sealedFileId, DocumentHash sealedHash)
    {
        return _documents.Count == 1
            ? MarkDocumentSealed(_documents[0].Id, sealedFileId, sealedHash, DateTime.UtcNow)
            : Result.Failure(
                new Error("Signature.Request.DocumentRequired", "DocumentId is required for multi-document requests.")
            );
    }

    public Result RecordCertificate(Guid certificateFileId)
    {
        if (Status != SignatureRequestStatus.Completed)
            return Result.Failure(
                new Error("Signature.Request.NotCompleted", "Certificate can only be attached to a completed request.")
            );

        if (certificateFileId == Guid.Empty)
            return Result.Failure(new Error("Signature.Request.CertificateFile", "CertificateFileId is required."));

        if (!AllDocumentsSealed())
            return Result.Failure(
                new Error(
                    "Signature.Request.DocumentsNotSealed",
                    "The certificate can only be attached after every document has been sealed."
                )
            );

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

        if (tokenExpirationHours is < 1 or > 720)
            return Result.Failure(
                new Error("Signature.Request.TokenExpiration", "Token expiration must be between 1 and 720 hours.")
            );

        return Result.Success();
    }

    /// <summary>
    /// Regla de edición: solo se puede modificar la solicitud en <c>Draft</c> o <c>Ready</c> (aún no
    /// enviada). Devuelve <see cref="Result.Failure"/> — NO lanza — para que la API responda 4xx en vez
    /// de 500 cuando el actor intenta editar una solicitud ya enviada/completada (p. ej. fijar el PIN).
    /// </summary>
    // La existencia de la categoría (sistema o custom del tenant) la valida el handler contra el repo;
    // aquí solo se guarda la forma (no vacía, dentro del largo).
    private static Result ValidateCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return Result.Failure(new Error("Signature.Request.Category", "Category is required."));
        if (category.Trim().Length > MaxCategoryLength)
            return Result.Failure(
                new Error("Signature.Request.Category", $"Category cannot exceed {MaxCategoryLength} characters.")
            );
        return Result.Success();
    }

    private Result EnsureCanBeEdited() =>
        Status is SignatureRequestStatus.Draft or SignatureRequestStatus.Ready ? Result.Success()
        : Status == SignatureRequestStatus.Scheduled
            ? Result.Failure(
                new Error(
                    "Signature.Request.Scheduled",
                    "This request is scheduled to send. Cancel the schedule to edit it."
                )
            )
        : Result.Failure(
            new Error(
                "Signature.Request.NotEditable",
                $"This request can no longer be edited (status {Status}); only draft or ready requests can be changed."
            )
        );

    /// <summary>
    /// Registra el jti del token recién emitido para un firmante y devuelve el jti anterior (o
    /// <c>null</c>) para revocarlo. Lo usan Send (primer token) y Resend (rota y revoca el viejo).
    /// </summary>
    public string? RotateSignerToken(Guid signerId, string tokenId)
    {
        var signer = FindSignerOrNull(signerId);
        return signer?.RotateCurrentToken(tokenId);
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

    private Result ValidateDocumentsForSend()
    {
        if (_documents.Count == 0)
            return Result.Failure(
                new Error("Signature.Request.NoDocuments", "The request must have at least one document.")
            );
        if (_documents.Any(document => document.DocumentHashPre is null))
            return Result.Failure(
                new Error("Signature.Request.NoDocumentHash", "Every document must be attached before sending.")
            );
        if (
            _documents.Any(document =>
                !_signers.Any(signer =>
                    signer.Fields.Any(field =>
                        field.DocumentId == document.Id
                        && field.Kind is SignatureFieldKind.Signature or SignatureFieldKind.Initials
                    )
                )
            )
        )
            return Result.Failure(
                new Error(
                    "Signature.Request.NoSignatureField",
                    "Every document must have at least one Signature or Initials field."
                )
            );
        if (_signers.Any(signer => signer.Fields.Count == 0))
            return Result.Failure(
                new Error(
                    "Signature.Request.SignerWithoutDocument",
                    "Every signer must participate in at least one document."
                )
            );
        return Result.Success();
    }

    private int NextDocumentOrder() => _documents.Count == 0 ? 1 : _documents.Max(document => document.Order) + 1;

    private RequestDocument? FindDocumentOrNull(Guid documentId) =>
        _documents.Find(document => document.Id == documentId);

    private void NormalizeDocumentOrder()
    {
        var ordered = _documents.OrderBy(document => document.Order).ToList();
        for (var index = 0; index < ordered.Count; index++)
            ordered[index].Reorder(index + 1);

        _documents.Clear();
        _documents.AddRange(ordered);
    }

    private bool AllSignersHaveSigned() => _signers.Count > 0 && _signers.All(s => s.Status == SignerStatus.Signed);

    /// <summary>
    /// Determina si <paramref name="signer"/> es el próximo pendiente cuando la solicitud
    /// exige firma secuencial. Considera "próximo" al de menor <c>Order</c> que aún no
    /// haya firmado ni rechazado.
    /// </summary>
    private bool IsSignerNextInSequence(Signer signer)
    {
        var next = _signers
            .Where(candidate => candidate.Status is SignerStatus.Pending or SignerStatus.InProgress)
            .OrderBy(candidate => candidate.Order)
            .FirstOrDefault();
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
