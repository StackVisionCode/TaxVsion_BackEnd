using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>El sellado y el certificado se suben declarados: solicitud, rol y cliente firmante.</summary>
public sealed partial class SignatureRequestCompletedConsumerTests
{
    [Fact]
    public async Task Sealed_and_certificate_uploads_carry_the_request_role_and_customer()
    {
        var customerId = Guid.NewGuid();
        var request = NewCompletedRequest(
            documents: 1,
            generateCertificate: true,
            certificateMode: CertificateGenerationMode.PerDocument,
            signerCustomerId: customerId
        );
        var storage = new FakeStorage();

        await SealPhaseAsync(request, storage, certificates: new FixedCertificateRenderer());

        Assert.Equal(2, storage.Staged.Count);
        var sealedUpload = storage.Staged.Single(upload => upload.FileName.EndsWith("_Signed.pdf"));
        var certificateUpload = storage.Staged.Single(upload => upload.FileName.EndsWith("_Certificate.pdf"));
        Assert.Equal(new SignatureFileSource(request.Id, "sealed", customerId), sealedUpload.Source);
        Assert.Equal(new SignatureFileSource(request.Id, "certificate", customerId), certificateUpload.Source);
    }

    [Fact]
    public async Task Without_customer_the_uploads_are_declared_without_customer()
    {
        var request = NewCompletedRequest(documents: 1);
        var storage = new FakeStorage();

        await SealPhaseAsync(request, storage);

        var upload = Assert.Single(storage.Staged);
        Assert.Equal(new SignatureFileSource(request.Id, "sealed", null), upload.Source);
    }
}
