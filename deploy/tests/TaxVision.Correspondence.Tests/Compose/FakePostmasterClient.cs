using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Tests.Compose;

internal sealed class FakePostmasterClient : IPostmasterClient
{
    public Result<SendDraftPostmasterResult> Response { get; set; } =
        Result.Success(new SendDraftPostmasterResult(Guid.NewGuid(), "provider-message-1"));

    public int CallCount { get; private set; }
    public IReadOnlyList<string>? LastTo { get; private set; }
    public IReadOnlyList<string>? LastCc { get; private set; }
    public IReadOnlyList<string>? LastBcc { get; private set; }

    public Task<Result<SendDraftPostmasterResult>> SendAsync(
        Guid tenantId,
        Guid draftId,
        Guid accountId,
        string subject,
        string html,
        string? text,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        IReadOnlyList<DraftAttachmentRef> attachments,
        ReplyContext? replyContext,
        CancellationToken ct = default
    )
    {
        CallCount++;
        LastTo = to;
        LastCc = cc;
        LastBcc = bcc;
        return Task.FromResult(Response);
    }
}
