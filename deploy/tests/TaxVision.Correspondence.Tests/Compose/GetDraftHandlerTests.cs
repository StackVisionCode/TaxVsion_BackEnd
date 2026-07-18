using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class GetDraftHandlerTests
{
    private static EmailAddress Address(string value) => EmailAddress.Create(value).Value;

    [Fact]
    public async Task Handle_WithKnownDraft_ReturnsTheFullDetailShape()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, customerId, accountId).Value;
        draft.AutoSave(
            "Tax question",
            "<p>Hello</p>",
            "Hello",
            [new DraftRecipientData(Address("a@example.com"), EmailRecipientType.To, "A")]
        );
        draft.AttachFile(DraftAttachmentRef.Create(Guid.NewGuid(), "file.pdf", "application/pdf", 100).Value);
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);

        var result = await GetDraftHandler.Handle(
            new GetDraftQuery(tenantId, draft.Id),
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var detail = result.Value;
        Assert.Equal(draft.Id, detail.DraftId);
        Assert.Equal(customerId, detail.CustomerId);
        Assert.Equal(accountId, detail.AccountId);
        Assert.Equal("Tax question", detail.Subject);
        Assert.Equal("<p>Hello</p>", detail.HtmlBody);
        Assert.Equal("Hello", detail.TextBody);
        Assert.Equal("Draft", detail.Status);
        Assert.Null(detail.ReplyContext);

        var recipient = Assert.Single(detail.Recipients);
        Assert.Equal("a@example.com", recipient.Address);
        Assert.Equal("To", recipient.Type);
        Assert.Equal("A", recipient.DisplayName);

        var attachment = Assert.Single(detail.Attachments);
        Assert.Equal("file.pdf", attachment.Filename);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(100, attachment.SizeBytes);
    }

    [Fact]
    public async Task Handle_WithUnknownDraft_ReturnsNotFound()
    {
        var drafts = new FakeDraftRepository();

        var result = await GetDraftHandler.Handle(
            new GetDraftQuery(Guid.NewGuid(), Guid.NewGuid()),
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithDraftFromAnotherTenant_ReturnsNotFound()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);

        var result = await GetDraftHandler.Handle(
            new GetDraftQuery(Guid.NewGuid(), draft.Id),
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.NotFound", result.Error.Code);
    }
}
