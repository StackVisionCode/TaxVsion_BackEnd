using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// P2 — flags de entrega: default histórico (documento firmado ON, certificado OFF), mutaciones
/// explícitas solo en Draft/Ready, y el certificado solo se entrega si se genera.
/// </summary>
public sealed class SignatureRequestDeliveryFlagsTests
{
    [Fact]
    public void Defaults_deliver_signed_document_but_not_certificate()
    {
        var request = NewDraft(generateCertificate: true);

        Assert.True(request.SendSignedDocumentToSigners);
        Assert.False(request.SendCertificateToSigners);
    }

    [Fact]
    public void Factory_cannot_enable_certificate_delivery_without_generating_it()
    {
        var request = SignatureRequest
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
                generateCertificate: false,
                sendSignedDocumentToSigners: true,
                sendCertificateToSigners: true
            )
            .Value;

        // Sin generación de certificado, la entrega del certificado queda apagada.
        Assert.False(request.SendCertificateToSigners);
    }

    [Fact]
    public void SetSignedDocumentDelivery_toggles_in_draft()
    {
        var request = NewDraft(generateCertificate: false);

        var result = request.SetSignedDocumentDelivery(false);

        Assert.True(result.IsSuccess);
        Assert.False(request.SendSignedDocumentToSigners);
    }

    [Fact]
    public void SetCertificateDelivery_requires_generation()
    {
        var request = NewDraft(generateCertificate: false);

        var result = request.SetCertificateDelivery(true);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.CertificateNotGenerated", result.Error.Code);
    }

    [Fact]
    public void SetCertificateDelivery_succeeds_when_generated()
    {
        var request = NewDraft(generateCertificate: true);

        var result = request.SetCertificateDelivery(true);

        Assert.True(result.IsSuccess);
        Assert.True(request.SendCertificateToSigners);
    }

    [Fact]
    public void Delivery_settings_are_locked_after_completion()
    {
        var request = NewInProgressCompleted();

        var result = request.SetSignedDocumentDelivery(false);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotEditable", result.Error.Code);
    }

    private static SignatureRequest NewDraft(bool generateCertificate) =>
        SignatureRequest
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
                generateCertificate: generateCertificate
            )
            .Value;

    private static SignatureRequest NewInProgressCompleted()
    {
        var draft = NewDraft(generateCertificate: false);
        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("The Signer").Value, null)
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        draft.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);
        return draft;
    }
}
