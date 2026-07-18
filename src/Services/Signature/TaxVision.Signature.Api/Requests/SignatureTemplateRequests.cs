using TaxVision.Signature.Application.Templates.Commands.Instantiate;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Api.Requests;

public sealed record CreateTemplateBody(
    string Title,
    string? Description,
    SignatureCategory Category,
    int DefaultTokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate
);

public sealed record UpdateTemplateMetadataBody(string Title, string? Description, SignatureCategory Category);

public sealed record UpdateTemplateDefaultsBody(
    int DefaultTokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate
);

public sealed record AddTemplateSlotBody(string Role, string DefaultLanguage);

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
    Guid OriginalFileId,
    IReadOnlyList<SlotBinding> SlotBindings,
    string? DescriptionOverride
);
