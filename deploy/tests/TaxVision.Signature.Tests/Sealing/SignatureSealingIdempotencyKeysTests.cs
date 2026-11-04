using TaxVision.Signature.Application.Sealing;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>El mismo sellado conserva su clave; un re-sellado (otro archivo) obtiene una distinta.</summary>
public sealed class SignatureSealingIdempotencyKeysTests
{
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();

    [Fact]
    public void The_same_seal_always_yields_the_same_key()
    {
        var sealedFileId = Guid.NewGuid();

        Assert.Equal(
            SignatureSealingIdempotencyKeys.DocumentSealed(RequestId, DocumentId, sealedFileId),
            SignatureSealingIdempotencyKeys.DocumentSealed(RequestId, DocumentId, sealedFileId)
        );
    }

    [Fact]
    public void A_reseal_with_a_new_file_yields_a_different_key()
    {
        Assert.NotEqual(
            SignatureSealingIdempotencyKeys.DocumentSealed(RequestId, DocumentId, Guid.NewGuid()),
            SignatureSealingIdempotencyKeys.DocumentSealed(RequestId, DocumentId, Guid.NewGuid())
        );
    }

    [Fact]
    public void Each_document_of_the_request_keeps_its_own_key()
    {
        var sealedFileId = Guid.NewGuid();

        Assert.NotEqual(
            SignatureSealingIdempotencyKeys.DocumentSealed(RequestId, DocumentId, sealedFileId),
            SignatureSealingIdempotencyKeys.DocumentSealed(RequestId, Guid.NewGuid(), sealedFileId)
        );
    }
}
