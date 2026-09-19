using BuildingBlocks.Domain;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Valor que un <see cref="Signer"/> escribió en un campo <c>Text</c> al firmar. Entidad
/// interna del aggregate root <see cref="SignatureRequest"/>: sólo se crea vía
/// <see cref="Signer.CaptureFieldValues"/>, nunca directamente por Application.
///
/// El valor es del acto de firma (por firmante + campo), separado del template del campo
/// (<see cref="SignatureField.Label"/>/<c>IsRequired</c>/<c>Kind</c>), y es lo que el motor
/// de sellado estampa sobre el PDF en la posición del campo.
/// </summary>
public sealed class SignedFieldValue : BaseEntity
{
    private SignedFieldValue() { }

    public Guid SignerId { get; private set; }
    public Guid FieldId { get; private set; }
    public string Value { get; private set; } = default!;
    public DateTime CapturedAtUtc { get; private set; }

    internal static SignedFieldValue Create(
        Guid signerId,
        Guid fieldId,
        SignatureFieldValue value,
        DateTime capturedAtUtc
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        return new SignedFieldValue
        {
            Id = Guid.NewGuid(),
            SignerId = signerId,
            FieldId = fieldId,
            Value = value.Value,
            CapturedAtUtc = capturedAtUtc,
        };
    }
}
