using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// P4 — captura de valores de campos de texto por el firmante. El aggregate valida propiedad
/// del campo, tipo Text y campos requeridos antes de anclar los valores.
/// </summary>
public sealed class SignerFieldValueCaptureTests
{
    [Fact]
    public void Captures_value_for_required_text_field()
    {
        var (request, signer, textFieldId) = NewInProgressWithTextField(isRequired: true);

        var result = request.CaptureSignerFieldValues(
            signer.Id,
            [new SignerFieldValueInput(textFieldId, "Spouse")],
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(signer.FieldValues);
        Assert.Equal(textFieldId, stored.FieldId);
        Assert.Equal("Spouse", stored.Value);
    }

    [Fact]
    public void Required_text_field_left_blank_fails()
    {
        var (request, signer, _) = NewInProgressWithTextField(isRequired: true);

        var result = request.CaptureSignerFieldValues(signer.Id, [], DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.FieldValue.RequiredMissing", result.Error.Code);
    }

    [Fact]
    public void Optional_text_field_left_blank_succeeds_without_storing()
    {
        var (request, signer, _) = NewInProgressWithTextField(isRequired: false);

        var result = request.CaptureSignerFieldValues(signer.Id, [], DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Empty(signer.FieldValues);
    }

    [Fact]
    public void Value_for_non_text_field_is_rejected()
    {
        var (request, signer, _) = NewInProgressWithTextField(isRequired: false);
        var signatureFieldId = signer.Fields.First(f => f.Kind == SignatureFieldKind.Signature).Id;

        var result = request.CaptureSignerFieldValues(
            signer.Id,
            [new SignerFieldValueInput(signatureFieldId, "nope")],
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.FieldValue.NotText", result.Error.Code);
    }

    [Fact]
    public void Value_for_unknown_field_is_rejected()
    {
        var (request, signer, _) = NewInProgressWithTextField(isRequired: false);

        var result = request.CaptureSignerFieldValues(
            signer.Id,
            [new SignerFieldValueInput(Guid.NewGuid(), "orphan")],
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.FieldValue.FieldMissing", result.Error.Code);
    }

    [Fact]
    public void Recapture_replaces_previous_values()
    {
        var (request, signer, textFieldId) = NewInProgressWithTextField(isRequired: true);

        request.CaptureSignerFieldValues(signer.Id, [new SignerFieldValueInput(textFieldId, "First")], DateTime.UtcNow);
        var second = request.CaptureSignerFieldValues(
            signer.Id,
            [new SignerFieldValueInput(textFieldId, "Second")],
            DateTime.UtcNow
        );

        Assert.True(second.IsSuccess);
        var stored = Assert.Single(signer.FieldValues);
        Assert.Equal("Second", stored.Value);
    }

    private static (SignatureRequest Request, Signer Signer, Guid TextFieldId) NewInProgressWithTextField(
        bool isRequired
    )
    {
        var draft = SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Test",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;
        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("The Signer").Value, null)
            .Value;

        var signaturePos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, signaturePos, null, false);

        var textPos = FieldPosition.Create(1, 0.1, 0.3, 0.3, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Text, textPos, "Relationship", isRequired);

        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);

        var textFieldId = signer.Fields.First(f => f.Kind == SignatureFieldKind.Text).Id;
        return (draft, signer, textFieldId);
    }
}
