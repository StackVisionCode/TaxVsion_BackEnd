using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageDetail;
using TaxVision.Postmaster.Application.Sending.Queries.ListSentMessages;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

namespace TaxVision.Postmaster.Tests.Sending;

/// <summary>"Email activity": listado y detalle contra el repositorio real (EF InMemory).</summary>
public sealed class ListSentMessagesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static PostmasterDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<PostmasterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );

    private static async Task<SentMessage> AddAsync(
        PostmasterDbContext db,
        string recipient,
        DeliveryOutcome? outcome = null,
        Guid? tenantId = null,
        DateTime? queuedAt = null
    )
    {
        var at = queuedAt ?? T0;
        var message = SentMessage
            .Queue(
                tenantId ?? TenantId,
                Guid.NewGuid().ToString("N"),
                "Welcome",
                "no-reply@taxoffix.com",
                EmailStream.Transactional,
                "system-smtp",
                null,
                null,
                null,
                null,
                null,
                at
            )
            .Value;
        var added = message.AddRecipient(recipient, RecipientType.To, null).Value;
        message.MarkAsSending();
        message.MarkAsSent("id", at);
        if (outcome is { } value)
            message.ApplyDeliveryFeedback(
                added.Id,
                DeliveryFeedback
                    .ForOutcome(value, at.AddMinutes(1), FeedbackSource.Smtp2Go, Guid.NewGuid().ToString("N"), "550")
                    .Value,
                null
            );
        await db.SentMessages.AddAsync(message);
        await db.SaveChangesAsync();
        return message;
    }

    private static Task<BuildingBlocks.Common.PagedResult<SentMessageSummaryDto>> ListAsync(
        PostmasterDbContext db,
        string? status = null,
        string? recipient = null,
        bool mask = false,
        int page = 1,
        int size = 25
    ) =>
        ListSentMessagesHandler.Handle(
            new ListSentMessagesQuery(
                TenantId,
                new SentMessageActivityFilter(status, recipient, null, null),
                page,
                size,
                mask
            ),
            new SentMessageRepository(db),
            CancellationToken.None
        );

    [Fact]
    public async Task List_shows_the_real_delivery_status()
    {
        await using var db = CreateContext();
        await AddAsync(db, "a@example.com", DeliveryOutcome.Delivered);
        await AddAsync(db, "b@example.com", DeliveryOutcome.HardBounced);
        await AddAsync(db, "c@example.com");

        var result = await ListAsync(db);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["Bounced", "Delivered", "Sent"], result.Items.Select(i => i.Status).Order());
    }

    [Theory]
    [InlineData(SentMessageActivityStatus.Delivered, "a@example.com")]
    [InlineData(SentMessageActivityStatus.Bounced, "b@example.com")]
    [InlineData(SentMessageActivityStatus.Sent, "c@example.com")]
    public async Task List_filters_by_status(string status, string expectedRecipient)
    {
        await using var db = CreateContext();
        await AddAsync(db, "a@example.com", DeliveryOutcome.Delivered);
        await AddAsync(db, "b@example.com", DeliveryOutcome.HardBounced);
        await AddAsync(db, "c@example.com");

        var result = await ListAsync(db, status: status);

        Assert.Equal(expectedRecipient, Assert.Single(Assert.Single(result.Items).Recipients));
    }

    [Fact]
    public async Task List_filters_by_recipient_and_masks_for_non_admins()
    {
        await using var db = CreateContext();
        await AddAsync(db, "maria@client.com");
        await AddAsync(db, "john@other.com");

        var result = await ListAsync(db, recipient: "Maria", mask: true);

        Assert.Equal("m***@client.com", Assert.Single(Assert.Single(result.Items).Recipients));
    }

    [Fact]
    public async Task List_only_shows_the_callers_tenant_newest_first_and_paged()
    {
        await using var db = CreateContext();
        await AddAsync(db, "old@example.com", queuedAt: T0);
        await AddAsync(db, "new@example.com", queuedAt: T0.AddHours(1));
        await AddAsync(db, "other@example.com", tenantId: Guid.NewGuid());

        var firstPage = await ListAsync(db, size: 1);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal("new@example.com", Assert.Single(Assert.Single(firstPage.Items).Recipients));
        Assert.True(firstPage.HasMore);
    }

    [Fact]
    public async Task Detail_has_the_timeline_and_masks_for_non_admins()
    {
        await using var db = CreateContext();
        var message = await AddAsync(db, "maria@client.com", DeliveryOutcome.HardBounced);

        var detail = await GetSentMessageDetailHandler.Handle(
            new GetSentMessageDetailQuery(TenantId, message.Id, MaskRecipients: true),
            new SentMessageRepository(db),
            CancellationToken.None
        );

        var recipient = Assert.Single(detail.Value.Recipients);
        Assert.Equal("m***@client.com", recipient.Address);
        Assert.Equal("HardBounced", recipient.Outcome);
        Assert.Equal("Bounced", detail.Value.Status);
        Assert.Equal(["Queued", "Sent", "Bounced"], detail.Value.Timeline.Select(e => e.EventType));
        Assert.Equal("m***@client.com", detail.Value.Timeline[^1].Recipient);
    }

    [Fact]
    public async Task Detail_of_another_tenant_is_not_found()
    {
        await using var db = CreateContext();
        var foreign = await AddAsync(db, "x@example.com", tenantId: Guid.NewGuid());

        var detail = await GetSentMessageDetailHandler.Handle(
            new GetSentMessageDetailQuery(TenantId, foreign.Id, MaskRecipients: false),
            new SentMessageRepository(db),
            CancellationToken.None
        );

        Assert.Equal("SentMessage.NotFound", detail.Error.Code);
    }
}
