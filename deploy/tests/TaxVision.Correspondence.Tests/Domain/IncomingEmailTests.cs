using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class IncomingEmailTests
{
    private static EmailAddress Address(string value) => EmailAddress.Create(value).Value;

    private static (Guid TenantId, Guid CustomerId, Guid EmailThreadId, Guid AccountId) ValidIds() =>
        (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Create_fails_when_customerId_is_empty()
    {
        var (tenantId, _, emailThreadId, accountId) = ValidIds();

        var result = IncomingEmail.Create(
            tenantId,
            Guid.Empty,
            emailThreadId,
            accountId,
            "gmail",
            "provider-msg-1",
            Address("customer@example.com"),
            null,
            "Subject",
            "Snippet",
            DateTime.UtcNow,
            hasAttachments: false,
            attachmentCount: 0
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.CustomerIdRequired", result.Error.Code);
    }

    [Fact]
    public void Create_fails_when_emailThreadId_is_empty()
    {
        var (tenantId, customerId, _, accountId) = ValidIds();

        var result = IncomingEmail.Create(
            tenantId,
            customerId,
            Guid.Empty,
            accountId,
            "gmail",
            "provider-msg-1",
            Address("customer@example.com"),
            null,
            "Subject",
            "Snippet",
            DateTime.UtcNow,
            hasAttachments: false,
            attachmentCount: 0
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.EmailThreadIdRequired", result.Error.Code);
    }

    [Fact]
    public void Create_succeeds_and_builds_recipients_and_attachments_from_the_input_lists()
    {
        var (tenantId, customerId, emailThreadId, accountId) = ValidIds();
        var receivedAt = DateTime.UtcNow;

        var result = IncomingEmail.Create(
            tenantId,
            customerId,
            emailThreadId,
            accountId,
            "gmail",
            "provider-msg-1",
            Address("Customer@Example.com"),
            "The Customer",
            "Subject",
            "Snippet",
            receivedAt,
            hasAttachments: true,
            attachmentCount: 1,
            internetMessageId: "<msg-1@example.com>",
            recipients:
            [
                new IncomingEmailRecipientData(
                    Address("tenant-user@example.com"),
                    EmailRecipientType.To,
                    "Tenant User"
                ),
            ],
            attachments:
            [
                new IncomingEmailAttachmentData("invoice.pdf", "application/pdf", 1024, "provider-att-1", false),
            ]
        );

        Assert.True(result.IsSuccess);
        var email = result.Value;
        Assert.NotEqual(Guid.Empty, email.Id);
        Assert.Equal(tenantId, email.TenantId);
        Assert.Equal(customerId, email.CustomerId);
        Assert.Equal(emailThreadId, email.EmailThreadId);
        Assert.Equal("customer@example.com", email.From);
        Assert.Equal(BodyStatus.BodyPending, email.BodyStatus);
        Assert.Null(email.BodyFetchedAtUtc);

        var recipient = Assert.Single(email.Recipients);
        Assert.Equal(email.Id, recipient.IncomingEmailId);
        Assert.Equal(tenantId, recipient.TenantId);
        Assert.Equal("tenant-user@example.com", recipient.Address);
        Assert.Equal(EmailRecipientType.To, recipient.Type);

        var attachment = Assert.Single(email.Attachments);
        Assert.Equal(email.Id, attachment.IncomingEmailId);
        Assert.Equal(tenantId, attachment.TenantId);
        Assert.Equal(AttachmentDownloadStatus.NotRequested, attachment.DownloadStatus);
        Assert.Null(attachment.CloudStorageFileId);
    }

    [Fact]
    public void MarkBodyFetched_transitions_to_ready_and_is_idempotent()
    {
        var (tenantId, customerId, emailThreadId, accountId) = ValidIds();

        var email = IncomingEmail
            .Create(
                tenantId,
                customerId,
                emailThreadId,
                accountId,
                "gmail",
                "provider-msg-1",
                Address("customer@example.com"),
                null,
                "Subject",
                "Snippet",
                DateTime.UtcNow,
                hasAttachments: false,
                attachmentCount: 0
            )
            .Value;

        email.MarkBodyFetched();
        Assert.Equal(BodyStatus.BodyReady, email.BodyStatus);
        var firstFetchedAt = email.BodyFetchedAtUtc;
        Assert.NotNull(firstFetchedAt);

        email.MarkBodyFetched();

        Assert.Equal(firstFetchedAt, email.BodyFetchedAtUtc);
    }
}
