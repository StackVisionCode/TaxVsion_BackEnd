using TaxVision.Signature.Application.Templates.Commands.Instantiate;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Api.Requests;

public sealed record CreateTemplateBody(
    string Title,
    string? Description,
    string Category,
    int DefaultTokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    // P7: documento base opcional del que se crea la plantilla; "from template" lo pre-selecciona.
    Guid? BaseDocumentFileId = null,
    // Defaults de entrega/recordatorio que "from template" copia a la solicitud.
    bool SendSignedDocumentToSigners = true,
    bool SendCertificateToSigners = false,
    bool AutoRemindersEnabled = true,
    int ReminderIntervalHours = 48
);

public sealed record UpdateTemplateMetadataBody(string Title, string? Description, string Category);

/// <summary>Practitioner PIN por defecto de la plantilla (Form 8879): 4–10 dígitos.</summary>
public sealed record SetTemplatePractitionerPinBody(string Pin);

/// <summary>P7: fija (o quita con null) el documento base de la plantilla; el archivo ya está en CloudStorage.</summary>
public sealed record SetTemplateBaseDocumentBody(Guid? BaseDocumentFileId);

public sealed record UpdateTemplateDefaultsBody(
    int DefaultTokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    bool SendSignedDocumentToSigners,
    bool SendCertificateToSigners,
    bool AutoRemindersEnabled,
    int ReminderIntervalHours
);

public sealed record AddTemplateSlotBody(
    string Role,
    string DefaultLanguage,
    SignerVerificationMethod? RequiredVerificationMethod = null
);

public sealed record UpdateTemplateSlotBody(
    string Role,
    string DefaultLanguage,
    SignerVerificationMethod? RequiredVerificationMethod = null
);

public sealed record PlaceTemplateFieldBody(
    int SlotOrder,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label,
    bool IsRequired
);

public sealed record InstantiateTemplateBody(
    IReadOnlyList<SlotBinding> SlotBindings,
    string? DescriptionOverride,
    // P7: opcional. Si no viene y la plantilla tiene documento base, se usa ese; si viene, override.
    Guid? OriginalFileId = null
);
