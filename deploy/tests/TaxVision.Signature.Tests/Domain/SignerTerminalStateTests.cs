using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// Con varios firmantes, quien ya firmó conserva su enlace mientras la solicitud sigue en curso: esos pedidos
/// llegan a las guardas de <see cref="Signer"/> y antes terminaban en 500.
/// </summary>
public sealed class SignerTerminalStateTests
{
    [Fact]
    public void Reject_AfterSigning_ReturnsTerminalFailure()
    {
        var (request, signed) = RequestWithOneSigned();

        var result = request.MarkSignerRejected(signed.Id, DateTime.UtcNow, "changed my mind", null, null);

        Assert.Equal("Signature.Signer.Terminal", result.Error.Code);
        Assert.Equal(SignerStatus.Signed, signed.Status);
        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);
    }

    [Fact]
    public void IssueVerificationChallenge_AfterSigning_ReturnsTerminalFailure()
    {
        var (request, signed) = RequestWithOneSigned();

        var result = request.IssueVerificationChallenge(
            signed.Id,
            SignerVerificationMethod.EmailOtp,
            "answer-hash",
            DateTime.UtcNow,
            TestSignatureLimits.Verification
        );

        Assert.Equal("Signature.Signer.Terminal", result.Error.Code);
    }

    private static (SignatureRequest Request, Signer Signed) RequestWithOneSigned()
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Two", null, "Fiscal", 72, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Return").Value;
        AddSigner(request, document.Id, "one@example.com", "Signer One");
        AddSigner(request, document.Id, "two@example.com", "Signer Two");
        var hash = DocumentHash.Create(new string('b', 64)).Value;
        Assert.True(request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(hash, 50).Value).IsSuccess);
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);

        var signed = request.Signers.First(signer => signer.Order == 1);
        Assert.True(request.MarkSignerSigned(signed.Id, DateTime.UtcNow, null, null).IsSuccess);
        return (request, signed);
    }

    private static void AddSigner(SignatureRequest request, Guid documentId, string email, string name)
    {
        var signer = request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create(email).Value,
                SignerFullName.Create(name).Value,
                null
            )
            .Value;
        Assert.True(
            request
                .PlaceField(
                    TestSignatureLimits.MaxFieldsPerSigner,
                    signer.Id,
                    documentId,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );
    }
}
