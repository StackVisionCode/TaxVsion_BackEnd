using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;

namespace TaxVision.Correspondence.Tests.Search;

/// <summary>Constructores de datos para los tests de búsqueda y de los listados con <c>q</c> (EF InMemory).</summary>
internal static class MailSearchTestData
{
    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; private set; } = tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public static CorrespondenceDbContext NewContext(Guid tenantId, string? databaseName = null) =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>()
                .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
                .Options,
            new TenantContext(tenantId)
        );

    public static EmailThread Thread(Guid tenantId, Guid customerId, string subject, DateTime? at = null) =>
        EmailThread.NewFromMessage(tenantId, customerId, subject, null, at ?? DateTime.UtcNow).Value;

    public static IncomingEmail Incoming(
        Guid tenantId,
        EmailThread thread,
        Guid accountId,
        string from = "ana@client.com",
        string? fromName = "Ana Perez",
        string? subject = null,
        string snippet = "Here it is",
        DateTime? at = null,
        IReadOnlyCollection<IncomingEmailAttachmentData>? attachments = null,
        params string[] to
    ) =>
        IncomingEmail
            .Create(
                tenantId,
                thread.CustomerId!.Value,
                thread.Id,
                accountId,
                "gmail",
                Guid.NewGuid().ToString("N"),
                EmailAddress.Create(from).Value,
                fromName,
                subject ?? thread.Subject,
                snippet,
                at ?? DateTime.UtcNow,
                hasAttachments: attachments is { Count: > 0 },
                attachmentCount: attachments?.Count ?? 0,
                recipients: to.Select(a => new IncomingEmailRecipientData(
                        EmailAddress.Create(a).Value,
                        EmailRecipientType.To,
                        null
                    ))
                    .ToList(),
                attachments: attachments
            )
            .Value;

    /// <summary>Un correo nuevo (sin conversación) ya enviado.</summary>
    public static Draft Sent(
        Guid tenantId,
        Guid? customerId,
        Guid accountId,
        string subject,
        string to,
        Guid? author = null,
        params DraftAttachmentRef[] attachments
    )
    {
        var draft = Open(tenantId, customerId, accountId, subject, to, author, attachments);
        draft.MarkSending();
        draft.MarkSent(Guid.NewGuid());
        return draft;
    }

    /// <summary>Un borrador abierto.</summary>
    public static Draft Open(
        Guid tenantId,
        Guid? customerId,
        Guid accountId,
        string subject,
        string to,
        Guid? author = null,
        params DraftAttachmentRef[] attachments
    )
    {
        var draft = Draft.CreateNew(tenantId, customerId, accountId, author ?? Guid.NewGuid()).Value;
        draft.AutoSave(
            subject,
            "<p>body</p>",
            "body",
            [new DraftRecipientData(EmailAddress.Create(to).Value, EmailRecipientType.To, null)]
        );
        foreach (var attachment in attachments)
            draft.AttachFile(attachment);
        return draft;
    }

    /// <summary>Un enviado (o borrador si <paramref name="send"/> es false) con varios destinatarios y nombres.</summary>
    public static Draft Outgoing(
        Guid tenantId,
        Guid? customerId,
        Guid accountId,
        string subject,
        bool send,
        Guid? author,
        params (string Address, string? Name, EmailRecipientType Type)[] recipients
    )
    {
        var draft = Draft.CreateNew(tenantId, customerId, accountId, author ?? Guid.NewGuid()).Value;
        draft.AutoSave(
            subject,
            "<p>body</p>",
            "body",
            recipients
                .Select(r => new DraftRecipientData(EmailAddress.Create(r.Address).Value, r.Type, r.Name))
                .ToList()
        );
        if (send)
        {
            draft.MarkSending();
            draft.MarkSent(Guid.NewGuid());
        }
        return draft;
    }

    public static DraftAttachmentRef File(string filename) =>
        DraftAttachmentRef.Create(Guid.NewGuid(), filename, "application/pdf", 1024).Value;
}
