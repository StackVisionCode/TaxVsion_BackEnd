using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>Tests de dominio del scaffold: la máquina de estados y las guardas. La suite completa
/// (idempotencia, hash, batch, aislamiento) llega con el slice E2E.</summary>
public sealed class DocumentGenerationTests
{
    private static DocumentGeneration NewRequested()
    {
        var type = DocumentType.Create("Invoice").Value;
        var key = TemplateKey.Create("billing.invoice.v1").Value;
        var result = DocumentGeneration.Request(
            tenantId: Guid.NewGuid(),
            documentType: type,
            templateKey: key,
            templateVersion: 1,
            outputFormat: DocumentOutputFormat.Pdf,
            owner: new GenerationOwner("Invoice", Guid.NewGuid()),
            sourceService: "billing",
            documentVersion: 1,
            priority: DocumentPriority.High,
            idempotencyKey: "idem-1",
            correlationId: "corr-1",
            causationId: null,
            nowUtc: DateTime.UtcNow
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void Request_starts_in_Requested()
    {
        Assert.Equal(DocumentGenerationStatus.Requested, NewRequested().Status);
    }

    [Fact]
    public void Happy_path_transitions_reach_Completed()
    {
        var g = NewRequested();
        var now = DateTime.UtcNow;
        var fileId = Guid.NewGuid();
        Assert.True(g.Queue(now).IsSuccess);
        Assert.True(g.StartRendering(now).IsSuccess);
        Assert.True(g.StartUploading(fileId, now).IsSuccess);
        Assert.True(g.MarkStored(new StorageReference(fileId, "application/pdf", 1234, null), now).IsSuccess);
        Assert.True(g.Complete(now).IsSuccess);
        Assert.Equal(DocumentGenerationStatus.Completed, g.Status);
    }

    [Fact]
    public void Invalid_transition_is_rejected()
    {
        var g = NewRequested();
        // No se puede subir sin haber renderizado.
        Assert.True(g.StartUploading(Guid.NewGuid(), DateTime.UtcNow).IsFailure);
    }

    [Fact]
    public void Completed_cannot_be_cancelled()
    {
        var g = NewRequested();
        var now = DateTime.UtcNow;
        var fileId = Guid.NewGuid();
        g.Queue(now);
        g.StartRendering(now);
        g.StartUploading(fileId, now);
        g.MarkStored(new StorageReference(fileId, "application/pdf", 1, null), now);
        g.Complete(now);
        Assert.True(g.Cancel(now).IsFailure);
    }

    [Fact]
    public void StartUploading_requires_a_FileId()
    {
        var g = NewRequested();
        var now = DateTime.UtcNow;
        g.Queue(now);
        g.StartRendering(now);
        Assert.True(g.StartUploading(Guid.Empty, now).IsFailure);
    }

    [Fact]
    public void MarkStored_rejects_a_FileId_that_differs_from_the_uploaded_one()
    {
        var g = NewRequested();
        var now = DateTime.UtcNow;
        g.Queue(now);
        g.StartRendering(now);
        Assert.True(g.StartUploading(Guid.NewGuid(), now).IsSuccess);
        // El FileId que reporta CloudStorage no coincide con el que Documents subió.
        var result = g.MarkStored(new StorageReference(Guid.NewGuid(), "application/pdf", 1, null), now);
        Assert.True(result.IsFailure);
        Assert.Equal("Documents.Generation.FileIdMismatch", result.Error.Code);
    }

    [Fact]
    public void Retry_only_from_Failed()
    {
        var g = NewRequested();
        Assert.True(g.RetryFromFailure(DateTime.UtcNow).IsFailure);
        g.Fail("Documents.Test", "boom", DateTime.UtcNow);
        Assert.True(g.RetryFromFailure(DateTime.UtcNow).IsSuccess);
        Assert.Equal(DocumentGenerationStatus.Queued, g.Status);
    }
}
