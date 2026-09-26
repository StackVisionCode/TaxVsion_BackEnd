using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Campo colocado sobre el documento que pertenece al PREPARADOR (no a un firmante cliente). Es la
/// contraparte de <see cref="SignatureField"/> para el canal paralelo del preparador (Form 8879): se
/// estampa con la firma reutilizable elegida (<see cref="SignatureRequest.PreparerSignatureFileId"/>) al
/// sellar, nunca antes, así que el firmante nunca la ve. Entidad interna del aggregate root; se agrega
/// vía <c>SignatureRequest.PlacePreparerField(...)</c>. Se modela aparte de SignatureField porque este
/// cuelga de un <see cref="Signer"/> (FK obligatoria) y el preparador no es un firmante.
/// </summary>
public sealed class PreparerField : BaseEntity
{
    public const int MaxLabelLength = 200;

    private PreparerField() { }

    public Guid SignatureRequestId { get; private set; }
    public SignatureFieldKind Kind { get; private set; }
    public FieldPosition Position { get; private set; } = default!;
    public string? Label { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    internal static Result<PreparerField> Create(
        Guid requestId,
        SignatureFieldKind kind,
        FieldPosition position,
        string? label
    )
    {
        if (requestId == Guid.Empty)
            return Result.Failure<PreparerField>(
                new Error("Signature.PreparerField.Request", "SignatureRequestId is required.")
            );

        ArgumentNullException.ThrowIfNull(position);

        var normalizedLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (normalizedLabel is { Length: > MaxLabelLength })
            return Result.Failure<PreparerField>(
                new Error("Signature.PreparerField.Label", $"Label cannot exceed {MaxLabelLength} characters.")
            );

        return Result.Success(
            new PreparerField
            {
                Id = Guid.NewGuid(),
                SignatureRequestId = requestId,
                Kind = kind,
                Position = position,
                Label = normalizedLabel,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
    }
}
