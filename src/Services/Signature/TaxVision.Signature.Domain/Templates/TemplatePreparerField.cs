using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Domain.Templates;

/// <summary>
/// Campo del PREPARADOR predefinido en una plantilla (no ligado a un slot). Al instanciar se copia como
/// <see cref="PreparerField"/> a la solicitud, con la misma posición y kind. Entidad interna del aggregate;
/// solo se crea/muta vía métodos del root.
/// </summary>
public sealed class TemplatePreparerField : BaseEntity
{
    public const int MaxLabelLength = 200;

    private TemplatePreparerField() { }

    public Guid SignatureTemplateId { get; private set; }
    public Guid TemplateDocumentId { get; private set; }
    public SignatureFieldKind Kind { get; private set; }
    public FieldPosition Position { get; private set; } = default!;
    public string? Label { get; private set; }

    internal static Result<TemplatePreparerField> Create(
        Guid templateId,
        Guid templateDocumentId,
        SignatureFieldKind kind,
        FieldPosition position,
        string? label
    )
    {
        if (templateId == Guid.Empty)
            return Result.Failure<TemplatePreparerField>(
                new Error("Signature.TemplatePreparerField.Template", "TemplateId is required.")
            );
        if (templateDocumentId == Guid.Empty)
            return Result.Failure<TemplatePreparerField>(
                new Error("Signature.TemplatePreparerField.Document", "TemplateDocumentId is required.")
            );
        ArgumentNullException.ThrowIfNull(position);

        var normalizedLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (normalizedLabel is { Length: > MaxLabelLength })
            return Result.Failure<TemplatePreparerField>(
                new Error("Signature.TemplatePreparerField.Label", $"Label cannot exceed {MaxLabelLength} characters.")
            );

        return Result.Success(
            new TemplatePreparerField
            {
                Id = Guid.NewGuid(),
                SignatureTemplateId = templateId,
                TemplateDocumentId = templateDocumentId,
                Kind = kind,
                Position = position,
                Label = normalizedLabel,
            }
        );
    }
}
