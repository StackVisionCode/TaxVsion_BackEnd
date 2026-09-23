using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Templates.ValueObjects;

namespace TaxVision.Signature.Domain.Templates;

/// <summary>
/// Plantilla reutilizable de firma. Encapsula:
/// <list type="bullet">
///   <item>Metadata (title/description/category) y defaults del proceso (expiración,
///     consent, secuencial, generate certificate).</item>
///   <item>La lista de <see cref="TemplateSignerSlot"/>s (roles que se rellenarán al
///     instanciar).</item>
///   <item>Los <see cref="TemplateField"/>s precolocados por slot.</item>
///   <item>El <see cref="SignatureTemplateStatus"/> ciclo de vida.</item>
/// </list>
///
/// <para>
/// Reglas de encapsulamiento: slots y fields nunca se exponen mutables. Cada mutación
/// tiene su método explícito con su regla concreta — no hay <c>Update(patch)</c>.
/// La edición sólo se permite en <c>Draft</c>; una vez publicada, la plantilla es
/// inmutable (garantiza reproducibilidad de las instancias).
/// </para>
/// </summary>
public sealed class SignatureTemplate : TenantEntity
{
    public const int MinTitleLength = 3;
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MinSlots = 1;
    public const int MaxSlots = 20;
    public const int DefaultReminderIntervalHours = 48;

    private readonly List<TemplateSignerSlot> _slots = [];
    private readonly List<TemplateField> _fields = [];

    private SignatureTemplate() { }

    public Guid CreatedByUserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public string Category { get; private set; } = default!;
    public SignatureTemplateStatus Status { get; private set; }

    public int DefaultTokenExpirationHours { get; private set; }
    public bool RequiresSequentialSigning { get; private set; }
    public bool RequiresConsent { get; private set; }
    public bool GenerateCertificate { get; private set; }

    /// <summary>Defaults de entrega/recordatorio que "from template" copia a la solicitud (mismos que la
    /// solicitud directa). Entregar el documento firmado a los firmantes; el certificado exige
    /// <see cref="GenerateCertificate"/>; recordatorios automáticos con su intervalo en horas.</summary>
    public bool SendSignedDocumentToSigners { get; private set; }
    public bool SendCertificateToSigners { get; private set; }
    public bool AutoRemindersEnabled { get; private set; }
    public int ReminderIntervalHours { get; private set; }

    /// <summary>
    /// Hash (PBKDF2) del Practitioner PIN por defecto de la plantilla (Form 8879). Opcional: si está,
    /// "from template" lo copia tal cual a la solicitud creada. NUNCA se expone en claro ni se devuelve
    /// por API — el DTO solo publica <see cref="RequiresPractitionerPin"/>.
    /// </summary>
    public string? PractitionerPinHash { get; private set; }

    /// <summary>true si la plantilla trae un Practitioner PIN por defecto configurado.</summary>
    public bool RequiresPractitionerPin => PractitionerPinHash is not null;

    /// <summary>
    /// Documento base opcional del que se creó la plantilla (P7). Si está presente, "from template"
    /// lo pre-selecciona como <c>OriginalFileId</c> de la solicitud sin re-subir; se puede override.
    /// </summary>
    public Guid? BaseDocumentFileId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }

    public IReadOnlyList<TemplateSignerSlot> Slots => _slots.AsReadOnly();
    public IReadOnlyList<TemplateField> Fields => _fields.AsReadOnly();

    // ------------------------------------------------------------------
    // Factory
    // ------------------------------------------------------------------

    public static Result<SignatureTemplate> CreateDraft(
        Guid tenantId,
        Guid createdByUserId,
        string title,
        string? description,
        string category,
        int defaultTokenExpirationHours,
        bool requiresSequentialSigning,
        bool requiresConsent,
        bool generateCertificate,
        Guid? baseDocumentFileId = null,
        bool sendSignedDocumentToSigners = true,
        bool sendCertificateToSigners = false,
        bool autoRemindersEnabled = true,
        int reminderIntervalHours = DefaultReminderIntervalHours
    )
    {
        var validation = ValidateFactoryInputs(
            tenantId,
            createdByUserId,
            title,
            description,
            defaultTokenExpirationHours
        );
        if (validation.IsFailure)
            return Result.Failure<SignatureTemplate>(validation.Error);

        var deliveryValidation = ValidateDelivery(
            generateCertificate,
            sendCertificateToSigners,
            autoRemindersEnabled,
            reminderIntervalHours
        );
        if (deliveryValidation.IsFailure)
            return Result.Failure<SignatureTemplate>(deliveryValidation.Error);

        var now = DateTime.UtcNow;
        var template = new SignatureTemplate
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = createdByUserId,
            Title = title.Trim(),
            Description = NormalizeDescription(description),
            Category = category.Trim(),
            Status = SignatureTemplateStatus.Draft,
            DefaultTokenExpirationHours = defaultTokenExpirationHours,
            RequiresSequentialSigning = requiresSequentialSigning,
            RequiresConsent = requiresConsent,
            GenerateCertificate = generateCertificate,
            SendSignedDocumentToSigners = sendSignedDocumentToSigners,
            SendCertificateToSigners = sendCertificateToSigners,
            AutoRemindersEnabled = autoRemindersEnabled,
            ReminderIntervalHours = reminderIntervalHours,
            BaseDocumentFileId = baseDocumentFileId == Guid.Empty ? null : baseDocumentFileId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        template.SetTenant(tenantId);
        return Result.Success(template);
    }

    // ------------------------------------------------------------------
    // Metadata / defaults
    // ------------------------------------------------------------------

    public Result UpdateMetadata(string title, string? description, string category)
    {
        EnsureDraft();

        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure(new Error("Signature.Template.Title", "Title is required."));
        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length is < MinTitleLength or > MaxTitleLength)
            return Result.Failure(
                new Error(
                    "Signature.Template.Title",
                    $"Title must be between {MinTitleLength} and {MaxTitleLength} characters."
                )
            );
        if (description is not null && description.Length > MaxDescriptionLength)
            return Result.Failure(
                new Error(
                    "Signature.Template.Description",
                    $"Description cannot exceed {MaxDescriptionLength} characters."
                )
            );

        Title = trimmedTitle;
        Description = NormalizeDescription(description);
        Category = category.Trim();
        Touch();
        return Result.Success();
    }

    public Result UpdateDefaults(
        int defaultTokenExpirationHours,
        bool requiresSequentialSigning,
        bool requiresConsent,
        bool generateCertificate,
        bool sendSignedDocumentToSigners,
        bool sendCertificateToSigners,
        bool autoRemindersEnabled,
        int reminderIntervalHours
    )
    {
        EnsureDraft();

        if (defaultTokenExpirationHours is < 1 or > 720)
            return Result.Failure(
                new Error(
                    "Signature.Template.TokenExpiration",
                    "Default token expiration must be between 1 and 720 hours."
                )
            );

        var deliveryValidation = ValidateDelivery(
            generateCertificate,
            sendCertificateToSigners,
            autoRemindersEnabled,
            reminderIntervalHours
        );
        if (deliveryValidation.IsFailure)
            return deliveryValidation;

        DefaultTokenExpirationHours = defaultTokenExpirationHours;
        RequiresSequentialSigning = requiresSequentialSigning;
        RequiresConsent = requiresConsent;
        GenerateCertificate = generateCertificate;
        SendSignedDocumentToSigners = sendSignedDocumentToSigners;
        SendCertificateToSigners = sendCertificateToSigners;
        AutoRemindersEnabled = autoRemindersEnabled;
        ReminderIntervalHours = reminderIntervalHours;
        Touch();
        return Result.Success();
    }

    /// <summary>Reglas de los defaults de entrega/recordatorio, compartidas por el factory y UpdateDefaults.</summary>
    private static Result ValidateDelivery(
        bool generateCertificate,
        bool sendCertificateToSigners,
        bool autoRemindersEnabled,
        int reminderIntervalHours
    )
    {
        if (sendCertificateToSigners && !generateCertificate)
            return Result.Failure(
                new Error(
                    "Signature.Template.CertificateNotGenerated",
                    "Enable certificate generation before delivering it to signers."
                )
            );

        if (autoRemindersEnabled && reminderIntervalHours is < 1 or > 720)
            return Result.Failure(
                new Error("Signature.Template.ReminderInterval", "Reminder interval must be between 1 and 720 hours.")
            );

        return Result.Success();
    }

    /// <summary>
    /// Fija el Practitioner PIN por defecto de la plantilla (recibe el hash ya calculado por la capa
    /// Application). Solo en <c>Draft</c>. Las solicitudes creadas desde la plantilla lo heredan.
    /// </summary>
    public Result SetPractitionerPin(string pinHash)
    {
        EnsureDraft();
        if (string.IsNullOrWhiteSpace(pinHash))
            return Result.Failure(new Error("Signature.Template.PinHash", "PIN hash is required."));

        PractitionerPinHash = pinHash;
        Touch();
        return Result.Success();
    }

    /// <summary>Quita el Practitioner PIN por defecto de la plantilla. Solo en <c>Draft</c>.</summary>
    public Result ClearPractitionerPin()
    {
        EnsureDraft();
        PractitionerPinHash = null;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Fija (o quita con <c>null</c>) el documento base de la plantilla (P7). Solo en <c>Draft</c>.
    /// El archivo lo sube/registra la capa Application; aquí solo se guarda la referencia.
    /// </summary>
    public Result SetBaseDocument(Guid? baseDocumentFileId)
    {
        EnsureDraft();
        BaseDocumentFileId = baseDocumentFileId == Guid.Empty ? null : baseDocumentFileId;
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Slots — cada operación con SU regla
    // ------------------------------------------------------------------

    public Result<TemplateSignerSlot> AddSlot(
        TemplateSlotRole role,
        string defaultLanguage,
        SignerVerificationMethod? requiredVerificationMethod = null
    )
    {
        EnsureDraft();

        if (_slots.Count >= MaxSlots)
            return Result.Failure<TemplateSignerSlot>(
                new Error("Signature.Template.TooManySlots", $"Slot count cannot exceed {MaxSlots}.")
            );

        var order = NextSlotOrder();
        var slotResult = TemplateSignerSlot.Create(Id, order, role, defaultLanguage, requiredVerificationMethod);
        if (slotResult.IsFailure)
            return slotResult;

        _slots.Add(slotResult.Value);
        Touch();
        return slotResult;
    }

    public Result RemoveSlot(int slotOrder)
    {
        EnsureDraft();

        var slot = FindSlotByOrderOrNull(slotOrder);
        if (slot is null)
            return Result.Failure(new Error("Signature.Template.SlotMissing", "Slot not found in this template."));

        _slots.Remove(slot);
        _fields.RemoveAll(f => f.SlotOrder == slotOrder);
        NormalizeSlotOrder();
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Edita en sitio rol/idioma/método de verificación de un slot existente (conserva su orden
    /// y sus campos). Solo en <c>Draft</c>.
    /// </summary>
    public Result UpdateSlot(
        int slotOrder,
        TemplateSlotRole role,
        string defaultLanguage,
        SignerVerificationMethod? requiredVerificationMethod
    )
    {
        EnsureDraft();

        var slot = FindSlotByOrderOrNull(slotOrder);
        if (slot is null)
            return Result.Failure(new Error("Signature.Template.SlotMissing", "Slot not found in this template."));

        var result = slot.Update(role, defaultLanguage, requiredVerificationMethod);
        if (result.IsFailure)
            return result;

        Touch();
        return Result.Success();
    }

    public Result ReorderSlots(IReadOnlyList<int> orderedSlotOrders)
    {
        ArgumentNullException.ThrowIfNull(orderedSlotOrders);
        EnsureDraft();

        if (orderedSlotOrders.Count != _slots.Count)
            return Result.Failure(
                new Error(
                    "Signature.Template.ReorderMismatch",
                    "The provided order does not match the current slot count."
                )
            );

        var byCurrentOrder = _slots.ToDictionary(s => s.Order);
        var applied = new List<TemplateSignerSlot>(orderedSlotOrders.Count);
        var newIndexForOldOrder = new Dictionary<int, int>(orderedSlotOrders.Count);

        for (var i = 0; i < orderedSlotOrders.Count; i++)
        {
            if (!byCurrentOrder.TryGetValue(orderedSlotOrders[i], out var slot))
                return Result.Failure(
                    new Error("Signature.Template.ReorderUnknownSlot", "Unknown slot order in the requested order.")
                );

            var newOrder = i + 1;
            newIndexForOldOrder[slot.Order] = newOrder;
            slot.Reorder(newOrder);
            applied.Add(slot);
        }

        RewireFieldsToNewSlotOrder(newIndexForOldOrder);
        _slots.Clear();
        _slots.AddRange(applied);
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Fields
    // ------------------------------------------------------------------

    public Result<TemplateField> PlaceField(
        int slotOrder,
        SignatureFieldKind kind,
        FieldPosition position,
        string? label,
        bool isRequired
    )
    {
        EnsureDraft();

        if (FindSlotByOrderOrNull(slotOrder) is null)
            return Result.Failure<TemplateField>(
                new Error("Signature.Template.SlotMissing", "Cannot place a field on an unknown slot.")
            );

        var fieldResult = TemplateField.Create(Id, slotOrder, kind, position, label, isRequired);
        if (fieldResult.IsFailure)
            return fieldResult;

        _fields.Add(fieldResult.Value);
        Touch();
        return fieldResult;
    }

    public Result RemoveField(Guid fieldId)
    {
        EnsureDraft();

        var field = _fields.Find(f => f.Id == fieldId);
        if (field is null)
            return Result.Failure(new Error("Signature.Template.FieldMissing", "Field not found in this template."));

        _fields.Remove(field);
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Ciclo de vida
    // ------------------------------------------------------------------

    public Result Publish()
    {
        if (Status == SignatureTemplateStatus.Published)
            return Result.Success();
        if (Status == SignatureTemplateStatus.Archived)
            return Result.Failure(
                new Error("Signature.Template.Archived", "An archived template cannot be published.")
            );

        if (_slots.Count < MinSlots)
            return Result.Failure(new Error("Signature.Template.NoSlots", "At least one slot is required to publish."));

        if (!HasAnyRequiredSignatureField())
            return Result.Failure(
                new Error(
                    "Signature.Template.NoSignatureField",
                    "At least one Signature or Initials field must be placed to publish."
                )
            );

        Status = SignatureTemplateStatus.Published;
        PublishedAtUtc = DateTime.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result Archive()
    {
        if (Status == SignatureTemplateStatus.Archived)
            return Result.Success();

        Status = SignatureTemplateStatus.Archived;
        ArchivedAtUtc = DateTime.UtcNow;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Devuelve una plantilla <c>Published</c> a <c>Draft</c> para poder editarla (roles, campos,
    /// settings). No afecta a las solicitudes ya instanciadas. Idempotente si ya está en Draft.
    /// </summary>
    public Result RevertToDraft()
    {
        if (Status == SignatureTemplateStatus.Draft)
            return Result.Success();
        if (Status == SignatureTemplateStatus.Archived)
            return Result.Failure(new Error("Signature.Template.Archived", "An archived template cannot be edited."));

        Status = SignatureTemplateStatus.Draft;
        PublishedAtUtc = null;
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
        int defaultTokenExpirationHours
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(new Error("Signature.Template.Tenant", "TenantId is required."));
        if (createdByUserId == Guid.Empty)
            return Result.Failure(new Error("Signature.Template.CreatedBy", "CreatedByUserId is required."));
        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure(new Error("Signature.Template.Title", "Title is required."));
        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length is < MinTitleLength or > MaxTitleLength)
            return Result.Failure(
                new Error(
                    "Signature.Template.Title",
                    $"Title must be between {MinTitleLength} and {MaxTitleLength} characters."
                )
            );
        if (description is not null && description.Length > MaxDescriptionLength)
            return Result.Failure(
                new Error(
                    "Signature.Template.Description",
                    $"Description cannot exceed {MaxDescriptionLength} characters."
                )
            );
        if (defaultTokenExpirationHours is < 1 or > 720)
            return Result.Failure(
                new Error(
                    "Signature.Template.TokenExpiration",
                    "Default token expiration must be between 1 and 720 hours."
                )
            );
        return Result.Success();
    }

    private void EnsureDraft()
    {
        if (Status != SignatureTemplateStatus.Draft)
            throw new InvalidOperationException($"SignatureTemplate {Id} cannot be edited in status {Status}.");
    }

    private int NextSlotOrder() => _slots.Count == 0 ? 1 : _slots.Max(s => s.Order) + 1;

    private TemplateSignerSlot? FindSlotByOrderOrNull(int slotOrder) => _slots.Find(s => s.Order == slotOrder);

    private void NormalizeSlotOrder()
    {
        var ordered = _slots.OrderBy(s => s.Order).ToList();
        var mapping = new Dictionary<int, int>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var oldOrder = ordered[i].Order;
            var newOrder = i + 1;
            if (oldOrder != newOrder)
                mapping[oldOrder] = newOrder;
            ordered[i].Reorder(newOrder);
        }

        if (mapping.Count > 0)
            RewireFieldsToNewSlotOrder(mapping);

        _slots.Clear();
        _slots.AddRange(ordered);
    }

    private void RewireFieldsToNewSlotOrder(IReadOnlyDictionary<int, int> oldToNew)
    {
        var rebuilt = new List<TemplateField>(_fields.Count);
        foreach (var field in _fields)
        {
            if (oldToNew.TryGetValue(field.SlotOrder, out var newOrder))
                rebuilt.Add(WithSlotOrder(field, newOrder));
            else
                rebuilt.Add(field);
        }
        _fields.Clear();
        _fields.AddRange(rebuilt);
    }

    private static TemplateField WithSlotOrder(TemplateField original, int newSlotOrder) =>
        TemplateField
            .Create(
                original.SignatureTemplateId,
                newSlotOrder,
                original.Kind,
                original.Position,
                original.Label,
                original.IsRequired
            )
            .Value;

    private bool HasAnyRequiredSignatureField() =>
        _fields.Any(f => f.Kind is SignatureFieldKind.Signature or SignatureFieldKind.Initials);

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        return description.Trim();
    }
}
