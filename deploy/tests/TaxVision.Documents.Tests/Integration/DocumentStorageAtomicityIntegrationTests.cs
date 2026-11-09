using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;
using Xunit;

namespace TaxVision.Documents.Tests.Integration;

/// <summary>
/// DOC-9 y DOC-3 contra SQL real, con la configuración de Wolverine de Program.cs y el
/// <c>DocumentStorageClient</c> real. Se escucha el envío de <c>SaveFileRequested</c> por una cola local:
/// contar sobres no ve lo que sale de memoria.
/// </summary>
public sealed class DocumentStorageAtomicityIntegrationTests(DocumentsOutboxFixture fixture)
    : IClassFixture<DocumentsOutboxFixture>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task SaveFileRequested_waits_for_the_commit_of_the_generation()
    {
        var tenantId = Guid.NewGuid();
        var watch = fixture.Commits.Watch(tenantId);

        await RequestInvoiceAsync(tenantId);

        Assert.False(await watch.SentBeforeCommit.Task.WaitAsync(Wait), "SaveFileRequested salió antes del commit.");
        Assert.True(await fixture.Saves.WaitForAsync(tenantId, Wait));
        Assert.Equal(DocumentGenerationStatus.Uploading, (await fixture.ReadGenerationAsync(tenantId))!.Status);
    }

    /// <summary>DOC-4 en SQL real: la búsqueda usa las tres columnas del índice único.</summary>
    [Fact]
    public async Task IdempotencyLookup_DoesNotCrossSourceServices()
    {
        var tenantId = Guid.NewGuid();
        var key = $"shared-key:{Guid.NewGuid():N}";
        using var scope = fixture.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentGenerationRepository>();
        var generation = DocumentGeneration
            .Request(
                tenantId: tenantId,
                documentType: DocumentType.Create("Invoice").Value,
                templateKey: TemplateKey.Create("billing.invoice.v1").Value,
                templateVersion: 1,
                outputFormat: DocumentOutputFormat.Pdf,
                owner: new GenerationOwner("Invoice", Guid.NewGuid()),
                sourceService: "tasks",
                documentVersion: 1,
                priority: DocumentPriority.Normal,
                idempotencyKey: key,
                correlationId: "it-doc4",
                causationId: null,
                nowUtc: DateTime.UtcNow
            )
            .Value;
        await repository.AddAsync(generation);
        await scope.ServiceProvider.GetRequiredService<BuildingBlocks.Persistence.IUnitOfWork>().SaveChangesAsync();

        Assert.Equal(generation.Id, (await repository.GetByIdempotencyKeyAsync(tenantId, "tasks", key))?.Id);
        Assert.Null(await repository.GetByIdempotencyKeyAsync(tenantId, "billing", key));
    }

    [Fact]
    public async Task FailureAfterUpload_LeavesNoSaveFileRequestedEnvelope()
    {
        var tenantId = Guid.NewGuid();
        var watch = fixture.Commits.Watch(tenantId);
        watch.FailCommit = true;

        await RequestInvoiceAsync(tenantId);

        await watch.SentBeforeCommit.Task.WaitAsync(Wait);
        Assert.False(
            await fixture.Saves.WaitForAsync(tenantId, SettleTime),
            "SaveFileRequested salió de un intento revertido."
        );
        Assert.NotEqual(DocumentGenerationStatus.Uploading, (await fixture.ReadGenerationAsync(tenantId))!.Status);
    }

    private async Task RequestInvoiceAsync(Guid tenantId)
    {
        var invoiceId = Guid.NewGuid();
        using var scope = fixture.Services.CreateScope();
        var accepted = await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<GenerateInvoiceDocumentResult>>(
                new GenerateInvoiceDocumentCommand(
                    TenantId: tenantId,
                    InvoiceId: invoiceId,
                    InvoiceNumber: "INV-IT-DOC9",
                    DocumentVersion: 1,
                    TemplateKey: "billing.invoice.v1",
                    TemplateVersion: 1,
                    TaxYear: 2026,
                    SourceService: "billing",
                    IdempotencyKey: $"invoice-pdf:{invoiceId:N}:v1:it",
                    CorrelationId: "it-doc9",
                    Invoice: new InvoicePayload(
                        "USD",
                        new DateOnly(2026, 11, 3),
                        null,
                        new InvoiceParty("Office", "12-3456789", null),
                        new InvoiceParty("Client", "", null),
                        [new InvoiceLine("Tax prep", 1, 350m, 350m)],
                        350m,
                        0m,
                        350m,
                        null
                    )
                )
            );
        Assert.True(accepted.IsSuccess);
    }
}
