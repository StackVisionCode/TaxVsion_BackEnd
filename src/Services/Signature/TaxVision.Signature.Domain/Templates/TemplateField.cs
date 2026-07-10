using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Domain.Templates;

/// <summary>
/// Campo predefinido de una plantilla, ligado a un <see cref="TemplateSignerSlot"/>
/// por su <c>SlotOrder</c>. Cuando la plantilla se instancia, se crea un
/// <c>SignatureField</c> con la misma posición y kind, atado al firmante real que
/// bindea ese slot. Entidad interna del aggregate; sólo se crea/muta via métodos
/// del root.
/// </summary>
public sealed class TemplateField : BaseEntity
{
    public const int MaxLabelLength = 200;

    private TemplateField() { }

    public Guid SignatureTemplateId { get; private set; }
    public int SlotOrder { get; private set; }
    public SignatureFieldKind Kind { get; private set; }
    public FieldPosition Position { get; private set; } = default!;
    public string? Label { get; private set; }
    public bool IsRequired { get; private set; }

    internal static Result<TemplateField> Create(
        Guid templateId,
        int slotOrder,
        SignatureFieldKind kind,
        FieldPosition position,
        string? label,
        bool isRequired
    )
    {
        if (templateId == Guid.Empty)
            return Result.Failure<TemplateField>(
                new Error("Signature.TemplateField.Template", "TemplateId is required.")
            );
        if (slotOrder < 1)
            return Result.Failure<TemplateField>(
                new Error("Signature.TemplateField.SlotOrder", "SlotOrder must be >= 1.")
            );
        ArgumentNullException.ThrowIfNull(position);

        var normalizedLabel = NormalizeLabel(label);
        if (normalizedLabel is { Length: > MaxLabelLength })
            return Result.Failure<TemplateField>(
                new Error("Signature.TemplateField.Label", $"Label cannot exceed {MaxLabelLength} characters.")
            );

        return Result.Success(
            new TemplateField
            {
                Id = Guid.NewGuid(),
                SignatureTemplateId = templateId,
                SlotOrder = slotOrder,
                Kind = kind,
                Position = position,
                Label = normalizedLabel,
                IsRequired = KindDefaultsToRequired(kind) || isRequired,
            }
        );
    }

    private static string? NormalizeLabel(string? candidate) =>
        string.IsNullOrWhiteSpace(candidate) ? null : candidate.Trim();

    private static bool KindDefaultsToRequired(SignatureFieldKind kind) =>
        kind is SignatureFieldKind.Signature or SignatureFieldKind.Initials;
}
