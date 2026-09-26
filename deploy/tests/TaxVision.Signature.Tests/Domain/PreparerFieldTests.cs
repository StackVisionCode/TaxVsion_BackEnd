using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using Xunit;

namespace TaxVision.Signature.Tests.Domain;

public sealed class PreparerFieldTests
{
    private static SignatureRequest NewDraft() =>
        SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Preparer field test",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

    private static FieldPosition Pos() => FieldPosition.Create(1, 0.1, 0.8, 0.2, 0.05).Value;

    private static SignatureRequest InProgress()
    {
        var draft = NewDraft();
        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Signer One").Value, null)
            .Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, Pos(), null, false);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    [Fact]
    public void PlacePreparerField_adds_a_field_in_draft()
    {
        var draft = NewDraft();

        var result = draft.PlacePreparerField(SignatureFieldKind.Signature, Pos(), "Preparer");

        Assert.True(result.IsSuccess);
        Assert.Single(draft.PreparerFields);
        Assert.Equal(SignatureFieldKind.Signature, draft.PreparerFields[0].Kind);
    }

    [Fact]
    public void PlacePreparerField_fails_after_send()
    {
        var request = InProgress();

        var result = request.PlacePreparerField(SignatureFieldKind.Signature, Pos(), null);

        Assert.True(result.IsFailure);
        Assert.Empty(request.PreparerFields);
    }

    [Fact]
    public void RemovePreparerField_removes_the_field()
    {
        var draft = NewDraft();
        var field = draft.PlacePreparerField(SignatureFieldKind.Signature, Pos(), null).Value;

        var result = draft.RemovePreparerField(field.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(draft.PreparerFields);
    }

    [Fact]
    public void RemovePreparerField_returns_not_found_when_missing()
    {
        var draft = NewDraft();

        var result = draft.RemovePreparerField(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.PreparerField.NotFound", result.Error.Code);
    }

    [Fact]
    public void SetPreparerSignature_stores_the_file_id()
    {
        var draft = NewDraft();
        var fileId = Guid.NewGuid();

        var result = draft.SetPreparerSignature(fileId);

        Assert.True(result.IsSuccess);
        Assert.Equal(fileId, draft.PreparerSignatureFileId);
    }

    [Fact]
    public void SetPreparerSignature_rejects_empty_file()
    {
        var draft = NewDraft();

        var result = draft.SetPreparerSignature(Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.PreparerField.File", result.Error.Code);
    }

    [Fact]
    public void SetPreparerSignature_fails_after_send()
    {
        var request = InProgress();

        var result = request.SetPreparerSignature(Guid.NewGuid());

        Assert.True(result.IsFailure);
    }
}
