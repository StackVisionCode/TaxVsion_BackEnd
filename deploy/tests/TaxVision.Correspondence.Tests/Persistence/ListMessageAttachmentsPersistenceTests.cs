using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

namespace TaxVision.Correspondence.Tests.Persistence;

/// <summary>
/// <see cref="ListMessageAttachmentsHandler"/> contra el repositorio EF real (no el fake), para
/// cubrir un mensaje con attachments en estados mixtos de <see cref="AttachmentDownloadStatus"/>.
/// El dominio (Fase 3) todavía no expone un mutator público para transicionar un attachment a
/// <see cref="AttachmentDownloadStatus.Downloaded"/> — eso es Fase 8/12, fuera de alcance acá — así
/// que este test simula el estado post-descarga escribiendo directo sobre el <c>ChangeTracker</c>
/// de EF, el mismo mecanismo con el que la fila realmente llegaría a ese estado en producción una
/// vez exista el flujo real.
/// </summary>
public sealed class ListMessageAttachmentsPersistenceTests
{
    // RBAC Fase 5 — EmailThread/IncomingEmail ahora son ITenantOwned; se setea el tenant "propio"
    // del test antes de consultar, igual que haría JwtTenantContextMiddleware en producción.
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static CorrespondenceDbContext CreateContext(string databaseName, ITenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    private static IncomingEmail NewIncomingEmailWithAttachments(Guid tenantId, Guid customerId, Guid emailThreadId) =>
        IncomingEmail
            .Create(
                tenantId,
                customerId,
                emailThreadId,
                Guid.NewGuid(),
                "gmail",
                "provider-msg-1",
                EmailAddress.Create("customer@example.com").Value,
                "The Customer",
                "Subject",
                "Snippet",
                DateTime.UtcNow,
                hasAttachments: true,
                attachmentCount: 3,
                attachments:
                [
                    new IncomingEmailAttachmentData("invoice.pdf", "application/pdf", 2048, "provider-att-1", false),
                    new IncomingEmailAttachmentData("logo.png", "image/png", 512, "provider-att-2", true),
                    new IncomingEmailAttachmentData(
                        "contract.docx",
                        "application/vnd.openxmlformats",
                        4096,
                        "provider-att-3",
                        false
                    ),
                ]
            )
            .Value;

    [Fact]
    public async Task Handle_WithMixedDownloadStatuses_MapsEachAttachmentCorrectlyAndLeaksNoBinaryField()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var emailId = Guid.Empty;
        var cloudStorageFileId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        await using (var db = CreateContext(databaseName, tenantContext))
        {
            var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
            var email = NewIncomingEmailWithAttachments(tenantId, customerId, thread.Id);
            emailId = email.Id;

            db.EmailThreads.Add(thread);
            db.IncomingEmails.Add(email);
            await db.SaveChangesAsync();

            // Simula: uno ya descargado (Fase 8/12, aún no construida), uno fallido, uno intacto.
            var downloaded = email.Attachments.Single(a => a.Filename == "invoice.pdf");
            var entry = db.Entry(downloaded);
            entry.Property(nameof(IncomingEmailAttachment.DownloadStatus)).CurrentValue =
                AttachmentDownloadStatus.Downloaded;
            entry.Property(nameof(IncomingEmailAttachment.CloudStorageFileId)).CurrentValue = cloudStorageFileId;
            entry.Property(nameof(IncomingEmailAttachment.DownloadedAtUtc)).CurrentValue = DateTime.UtcNow;

            var failed = email.Attachments.Single(a => a.Filename == "logo.png");
            db.Entry(failed).Property(nameof(IncomingEmailAttachment.DownloadStatus)).CurrentValue =
                AttachmentDownloadStatus.Failed;

            await db.SaveChangesAsync();
        }

        await using var reloadDb = CreateContext(databaseName, tenantContext);
        var repository = new IncomingEmailRepository(reloadDb);

        var result = await ListMessageAttachmentsHandler.Handle(
            new ListMessageAttachmentsQuery(tenantId, emailId),
            repository,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);

        var downloadedSummary = result.Value.Single(a => a.Filename == "invoice.pdf");
        Assert.Equal("Downloaded", downloadedSummary.DownloadStatus);
        Assert.Equal(cloudStorageFileId, downloadedSummary.CloudStorageFileId);

        var failedSummary = result.Value.Single(a => a.Filename == "logo.png");
        Assert.Equal("Failed", failedSummary.DownloadStatus);
        Assert.Null(failedSummary.CloudStorageFileId);

        var untouchedSummary = result.Value.Single(a => a.Filename == "contract.docx");
        Assert.Equal("NotRequested", untouchedSummary.DownloadStatus);
        Assert.Null(untouchedSummary.CloudStorageFileId);

        // Ningún campo del DTO expone binario ni FailureReason — solo los 7 campos del plan §21.
        var properties = typeof(AttachmentSummary).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(
            new[]
            {
                "AttachmentId",
                "CloudStorageFileId",
                "ContentType",
                "DownloadStatus",
                "Filename",
                "IsInline",
                "SizeBytes",
            },
            properties
        );
    }
}
