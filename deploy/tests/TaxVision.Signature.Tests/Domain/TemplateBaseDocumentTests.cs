using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// P7 — documento base de la plantilla: opcional en el factory, mutable en Draft, y no editable
/// una vez publicada.
/// </summary>
public sealed class TemplateBaseDocumentTests
{
    [Fact]
    public void Factory_defaults_to_no_base_document()
    {
        var template = NewDraft();

        Assert.Null(template.BaseDocumentFileId);
    }

    [Fact]
    public void Factory_can_carry_a_base_document()
    {
        var fileId = Guid.NewGuid();

        var template = SignatureTemplate
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Standard form",
                null,
                SignatureCategory.Fiscal,
                defaultTokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: true,
                generateCertificate: true,
                baseDocumentFileId: fileId
            )
            .Value;

        Assert.Equal(fileId, template.BaseDocumentFileId);
    }

    [Fact]
    public void SetBaseDocument_sets_and_clears_in_draft()
    {
        var template = NewDraft();
        var fileId = Guid.NewGuid();

        Assert.True(template.SetBaseDocument(fileId).IsSuccess);
        Assert.Equal(fileId, template.BaseDocumentFileId);

        Assert.True(template.SetBaseDocument(null).IsSuccess);
        Assert.Null(template.BaseDocumentFileId);
    }

    [Fact]
    public void SetBaseDocument_throws_after_publish()
    {
        var template = NewPublished();

        Assert.Throws<InvalidOperationException>(() => template.SetBaseDocument(Guid.NewGuid()));
    }

    private static SignatureTemplate NewDraft() =>
        SignatureTemplate
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Standard form",
                null,
                SignatureCategory.Fiscal,
                defaultTokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: true,
                generateCertificate: true
            )
            .Value;

    private static SignatureTemplate NewPublished()
    {
        var template = NewDraft();
        var slot = template
            .AddSlot(TaxVision.Signature.Domain.Templates.ValueObjects.TemplateSlotRole.Create("Signer").Value, "En")
            .Value;
        var pos = TaxVision.Signature.Domain.Requests.ValueObjects.FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        template.PlaceField(slot.Order, SignatureFieldKind.Signature, pos, null, true);
        template.Publish();
        return template;
    }
}
