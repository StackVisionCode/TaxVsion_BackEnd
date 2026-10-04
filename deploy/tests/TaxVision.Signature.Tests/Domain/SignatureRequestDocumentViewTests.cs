using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>F5 — RecordSignerDocumentFirstView: idempotente, no terminal, campo propio.</summary>
public sealed class SignatureRequestDocumentViewTests
{
    [Fact]
    public void RecordSignerDocumentFirstView_sets_the_timestamp_the_first_time()
    {
        var (request, signerId) = InProgressWithSigner();
        var at = DateTime.UtcNow;

        var result = request.RecordSignerDocumentFirstView(signerId, at, "203.0.113.1", "UA");

        Assert.True(result.IsSuccess);
        var signer = request.Signers.First();
        Assert.Equal(at, signer.DocumentFirstViewedAtUtc);
        Assert.Null(signer.FirstViewedAtUtc); // F5 no toca el campo histórico
    }

    [Fact]
    public void RecordSignerDocumentFirstView_is_idempotent()
    {
        var (request, signerId) = InProgressWithSigner();
        var first = DateTime.UtcNow;
        request.RecordSignerDocumentFirstView(signerId, first, null, null);

        var second = request.RecordSignerDocumentFirstView(signerId, first.AddHours(1), null, null);

        Assert.True(second.IsSuccess);
        Assert.Equal(first, request.Signers.First().DocumentFirstViewedAtUtc);
    }

    [Fact]
    public void RecordSignerDocumentFirstView_rejects_terminal_requests()
    {
        var (request, signerId) = InProgressWithSigner();
        request.Cancel(DateTime.UtcNow);

        var result = request.RecordSignerDocumentFirstView(signerId, DateTime.UtcNow, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.Terminal", result.Error.Code);
    }

    [Fact]
    public void RecordSignerDocumentFirstView_rejects_missing_signer()
    {
        var (request, _) = InProgressWithSigner();

        var result = request.RecordSignerDocumentFirstView(Guid.NewGuid(), DateTime.UtcNow, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.SignerMissing", result.Error.Code);
    }

    private static (SignatureRequest request, Guid signerId) InProgressWithSigner()
    {
        var request = SignatureRequest
            .CreateDraft(
                tenantId: Guid.NewGuid(),
                createdByUserId: Guid.NewGuid(),
                title: "F5 document view",
                description: null,
                category: "ConsentToDisclose",
                originalFileId: Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;
        var signer = request
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Sam Signer").Value, null)
            .Value;
        var position = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        request.PlaceField(signer.Id, SignatureFieldKind.Signature, position, null, false);
        request.AttachOriginalHash(DocumentHash.Create(new string('a', 64)).Value);
        request.Send(DateTime.UtcNow);
        return (request, signer.Id);
    }
}
