using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Customers;
using TaxVision.Correspondence.Infrastructure.Postmaster;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Infrastructure;

/// <summary>
/// <see cref="PostmasterClient"/> contra un HTTP falso (nunca el Postmaster real), encadenado con
/// <see cref="SendDraftHandler"/> para ver qué pasa con el borrador.
/// </summary>
public sealed class PostmasterClientTests
{
    private sealed class StaticHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
            );
    }

    private sealed class FakeTokenAcquirer : ICorrespondenceServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("fake-token");
    }

    private static PostmasterClient NewClient(HttpStatusCode status, string json) =>
        new(
            new HttpClient(new StaticHandler(status, json)) { BaseAddress = new Uri("http://postmaster.test/") },
            new FakeTokenAcquirer(),
            NullLogger<PostmasterClient>.Instance
        );

    private static async Task<(Draft Draft, BuildingBlocks.Results.Result<SendDraftResult> Result)> SendAsync(
        PostmasterClient client
    )
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        var saved = draft.AutoSave(
            "Subject",
            "<p>body</p>",
            null,
            [new DraftRecipientData(EmailAddress.Create("customer@example.com").Value, EmailRecipientType.To, null)]
        );
        Assert.True(saved.IsSuccess);
        await drafts.AddAsync(draft);

        var result = await SendDraftHandler.Handle(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1"),
            drafts,
            client,
            new FakeCorrespondenceAuditLogRepository(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            NullLogger<SendDraftCommand>.Instance,
            CancellationToken.None
        );
        return (draft, result);
    }

    // COR-17: el 202 de Postmaster significa "envío en curso"; el borrador no se marca enviado.
    [Fact]
    public async Task SendAsync_Accepted_DraftStaysSending()
    {
        var sentMessageId = Guid.NewGuid();
        var client = NewClient(
            HttpStatusCode.Accepted,
            $$"""{"sentMessageId":"{{sentMessageId}}","providerMessageId":null,"inProgress":true}"""
        );

        var (draft, result) = await SendAsync(client);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.InProgress);
        Assert.Equal(DraftStatus.Sending, draft.Status);
        Assert.Null(draft.SentMessageId);
    }

    [Fact]
    public async Task SendAsync_Ok_MarksSentAndKeepsInternetMessageId()
    {
        var sentMessageId = Guid.NewGuid();
        var client = NewClient(
            HttpStatusCode.OK,
            $$"""{"sentMessageId":"{{sentMessageId}}","providerMessageId":"p-1","internetMessageId":"<pm.1@tenant.test>"}"""
        );

        var (draft, result) = await SendAsync(client);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.InProgress);
        Assert.Equal(DraftStatus.Sent, draft.Status);
        Assert.Equal(sentMessageId, draft.SentMessageId);
        Assert.Equal("pm.1@tenant.test", draft.InternetMessageId);
    }
}
