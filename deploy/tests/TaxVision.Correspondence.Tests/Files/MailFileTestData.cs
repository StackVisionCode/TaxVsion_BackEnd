using BuildingBlocks.Contracts.Files;
using TaxVision.Correspondence.Application.Files;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

namespace TaxVision.Correspondence.Tests.Files;

/// <summary>Datos de correo con adjuntos para la visibilidad por archivo (EF InMemory).</summary>
internal static class MailFileTestData
{
    public static EmailThread Thread(Guid tenant, Guid? customer, string subject = "W-2", DateTime? at = null) =>
        EmailThread.NewFromMessage(tenant, customer, subject, null, at ?? DateTime.UtcNow).Value;

    /// <summary>Un entrante del hilo con un adjunto; si <paramref name="fileId"/> viene, ya descargado.</summary>
    public static IncomingEmail Incoming(EmailThread thread, Guid account, Guid? fileId, DateTime? at = null)
    {
        var email = IncomingEmail
            .Create(
                thread.TenantId,
                thread.CustomerId,
                thread.Id,
                account,
                "gmail",
                Guid.NewGuid().ToString("N"),
                EmailAddress.Create("ana@client.com").Value,
                null,
                thread.Subject,
                "",
                at ?? DateTime.UtcNow,
                hasAttachments: true,
                attachmentCount: 1,
                attachments: [new IncomingEmailAttachmentData("w2.pdf", "application/pdf", 10, "att-1", false, "1")]
            )
            .Value;
        if (fileId is { } id)
        {
            var attachment = email.Attachments.Single();
            attachment.MarkInProgress();
            attachment.MarkDownloaded(id);
        }
        return email;
    }

    /// <summary>Un enviado (correo nuevo, sin hilo) con un adjunto.</summary>
    public static Draft Sent(Guid tenant, Guid? customer, Guid account, Guid fileId, string subject = "Engagement")
    {
        var draft = Draft.CreateNew(tenant, customer, account, Guid.NewGuid()).Value;
        draft.AutoSave(
            subject,
            "<p>body</p>",
            null,
            [new DraftRecipientData(EmailAddress.Create("ana@client.com").Value, EmailRecipientType.To, null)]
        );
        draft.AttachFile(DraftAttachmentRef.Create(fileId, "w2.pdf", "application/pdf", 10).Value);
        draft.MarkSending();
        draft.MarkSent(Guid.NewGuid());
        return draft;
    }

    public static Task<FileVisibilityResponse> AskAsync(
        CorrespondenceDbContext db,
        Guid tenant,
        IReadOnlyList<Guid> fileIds,
        IReadOnlyCollection<Guid>? visible,
        Guid? restrictTo = null,
        Guid? viewer = null,
        bool canDownloadIncoming = true,
        string? purpose = null
    ) =>
        GetVisibleMailFilesHandler.Handle(
            new GetVisibleMailFilesQuery(
                tenant,
                fileIds,
                visible,
                restrictTo,
                viewer ?? Guid.NewGuid(),
                canDownloadIncoming,
                purpose
            ),
            new MailFileVisibilityReader(db),
            CancellationToken.None
        );
}
