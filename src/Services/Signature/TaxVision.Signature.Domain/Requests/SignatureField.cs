using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Campo colocado sobre el documento asignado a un <see cref="Signer"/>. Entidad
/// interna del aggregate root <see cref="SignatureRequest"/> — nunca se crea
/// directamente por Application; se agrega via <c>SignatureRequest.PlaceField(...)</c>.
///
/// Se identifica por <c>Id</c> propio (Guid) y contiene la posición normalizada, el
/// tipo (Signature/Initials/Date/Text/Checkbox), y las reglas específicas del tipo.
/// </summary>
public sealed class SignatureField : BaseEntity
{
    public const int MaxLabelLength = 200;

    private SignatureField() { }

    public Guid SignatureRequestId { get; private set; }
    public Guid SignerId { get; private set; }
    public SignatureFieldKind Kind { get; private set; }
    public FieldPosition Position { get; private set; } = default!;

    /// <summary>Etiqueta descriptiva para el firmante (ej. "Firma del contribuyente").</summary>
    public string? Label { get; private set; }

    /// <summary>Marca el campo como obligatorio. Aplica a Text, Checkbox e Initials.</summary>
    public bool IsRequired { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    internal static Result<SignatureField> Create(
        Guid requestId,
        Guid signerId,
        SignatureFieldKind kind,
        FieldPosition position,
        string? label,
        bool isRequired
    )
    {
        if (requestId == Guid.Empty)
            return Result.Failure<SignatureField>(
                new Error("Signature.Field.Request", "SignatureRequestId is required.")
            );

        if (signerId == Guid.Empty)
            return Result.Failure<SignatureField>(new Error("Signature.Field.Signer", "SignerId is required."));

        ArgumentNullException.ThrowIfNull(position);

        var normalizedLabel = NormalizeLabel(label);
        if (normalizedLabel is { Length: > MaxLabelLength })
            return Result.Failure<SignatureField>(
                new Error("Signature.Field.Label", $"Label cannot exceed {MaxLabelLength} characters.")
            );

        return Result.Success(
            new SignatureField
            {
                Id = Guid.NewGuid(),
                SignatureRequestId = requestId,
                SignerId = signerId,
                Kind = kind,
                Position = position,
                Label = normalizedLabel,
                IsRequired = KindDefaultsToRequired(kind) || isRequired,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
    }

    private static string? NormalizeLabel(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;
        return candidate.Trim();
    }

    private static bool KindDefaultsToRequired(SignatureFieldKind kind) =>
        kind is SignatureFieldKind.Signature or SignatureFieldKind.Initials;
}
