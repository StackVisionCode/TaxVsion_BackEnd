using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class ReplyContextTests
{
    [Fact]
    public void Create_succeeds_and_keeps_references_as_a_list()
    {
        var incomingEmailId = Guid.NewGuid();
        var emailThreadId = Guid.NewGuid();

        var result = ReplyContext.Create(
            incomingEmailId,
            emailThreadId,
            "<original@example.com>",
            ["<a@example.com>", "<b@example.com>"],
            "provider-msg-original"
        );

        Assert.True(result.IsSuccess);
        var context = result.Value;
        Assert.Equal(incomingEmailId, context.IncomingEmailId);
        Assert.Equal(emailThreadId, context.EmailThreadId);
        Assert.Equal("<original@example.com>", context.InReplyToInternetMessageId);
        Assert.Equal("provider-msg-original", context.ReplyToProviderMessageId);
        Assert.Equal(["<a@example.com>", "<b@example.com>"], context.References);
    }

    [Fact]
    public void Create_defaults_references_to_an_empty_list_when_null()
    {
        var result = ReplyContext.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, null);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.References);
    }

    [Fact]
    public void Create_fails_when_incomingEmailId_is_empty()
    {
        var result = ReplyContext.Create(Guid.Empty, Guid.NewGuid(), null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("ReplyContext.IncomingEmailIdRequired", result.Error.Code);
    }

    [Fact]
    public void Create_fails_when_emailThreadId_is_empty()
    {
        var result = ReplyContext.Create(Guid.NewGuid(), Guid.Empty, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("ReplyContext.EmailThreadIdRequired", result.Error.Code);
    }

    [Fact]
    public void Create_fails_when_inReplyToInternetMessageId_exceeds_the_max_length()
    {
        var tooLong = new string('x', ReplyContext.InReplyToInternetMessageIdMaxLength + 1);

        var result = ReplyContext.Create(Guid.NewGuid(), Guid.NewGuid(), tooLong, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("ReplyContext.InReplyToInternetMessageIdTooLong", result.Error.Code);
    }
}
