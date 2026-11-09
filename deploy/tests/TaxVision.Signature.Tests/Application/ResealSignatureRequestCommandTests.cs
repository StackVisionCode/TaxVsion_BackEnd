using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Signature.Application.Admin;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Application.Offboarding;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Application;

/// <summary>SIG-2: el reseal manual limpia la marca de fallo y vuelve a pedir el sellado de lo pendiente.</summary>
public sealed class ResealSignatureRequestCommandTests
{
    [Fact]
    public async Task FailedSealing_ClearsMarkAndRequestsSealingOfUnsealedDocuments()
    {
        var request = CompletedRequest();
        request.MarkSealingFailed(DateTime.UtcNow);
        var bus = new CapturingBus();
        var unitOfWork = new CountingUnitOfWork();

        var result = await ResealSignatureRequestHandler.Handle(
            new ResealSignatureRequestCommand(request.TenantId, request.Id),
            new SingleRequestRepository(request),
            unitOfWork,
            bus,
            new FixedCorrelation(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Null(request.SealingFailedAtUtc);
        Assert.Equal(1, unitOfWork.Saves);
        var ready = Assert.Single(bus.Messages.OfType<SignatureDocumentsReadyForSealingIntegrationEvent>());
        Assert.Equal(request.Documents.Select(d => d.Id), ready.DocumentIds);
    }

    [Fact]
    public async Task InProgressRequest_IsRejected()
    {
        var request = CompletedRequest(sign: false);

        var result = await ResealSignatureRequestHandler.Handle(
            new ResealSignatureRequestCommand(request.TenantId, request.Id),
            new SingleRequestRepository(request),
            new CountingUnitOfWork(),
            new CapturingBus(),
            new FixedCorrelation(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
    }

    private static SignatureRequest CompletedRequest(bool sign = true)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Reseal", null, "Fiscal", 72, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Return").Value;
        var signer = request
            .AddSigner(
                limits,
                SignerEmail.Create("s@example.test").Value,
                SignerFullName.Create("Signer One").Value,
                null
            )
            .Value;
        Assert.True(
            request
                .PlaceField(
                    TestSignatureLimits.MaxFieldsPerSigner,
                    signer.Id,
                    document.Id,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );
        Assert.True(
            request
                .AttachVerifiedDocument(
                    document.Id,
                    VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 5).Value
                )
                .IsSuccess
        );
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        if (sign)
            Assert.True(request.MarkSignerSigned(signer.Id, DateTime.UtcNow, "127.0.0.1", "tests").IsSuccess);
        return request;
    }
}
