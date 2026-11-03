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
                SentMessageActivityFilter.Create(status, recipient, null, null).Value,
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

    /// <summary>Un correo a dos destinatarios: uno entregado y el otro sin noticias.</summary>
    private static async Task<SentMessage> AddTwoRecipientsAsync(PostmasterDbContext db)
    {
        var message = SentMessage
            .Queue(
                TenantId,
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
                T0
            )
            .Value;
        var delivered = message.AddRecipient("delivered@example.com", RecipientType.To, null).Value;
        message.AddRecipient("waiting@example.com", RecipientType.To, null);
        message.MarkAsSending();
        message.MarkAsSent("id", T0);
        message.ApplyDeliveryFeedback(
            delivered.Id,
            DeliveryFeedback
                .ForOutcome(DeliveryOutcome.Delivered, T0.AddMinutes(1), FeedbackSource.Smtp2Go, "evt-d", null)
                .Value,
            null
        );
        await db.SentMessages.AddAsync(message);
        await db.SaveChangesAsync();
        return message;
    }

    [Fact]
    public async Task List_shows_a_mixed_message_only_under_the_tab_of_its_displayed_status()
    {
        await using var db = CreateContext();
        await AddTwoRecipientsAsync(db);

        var all = await ListAsync(db);
        var sent = await ListAsync(db, status: SentMessageActivityStatus.Sent);
        var delivered = await ListAsync(db, status: SentMessageActivityStatus.Delivered);

        // Uno sigue sin noticias: la fila dice "Sent" y solo aparece en esa pestaña.
        Assert.Equal(SentMessageActivityStatus.Sent, Assert.Single(all.Items).Status);
        Assert.Equal(SentMessageActivityStatus.Sent, Assert.Single(sent.Items).Status);
        Assert.Empty(delivered.Items);
    }

    [Fact]
    public async Task List_shows_a_delayed_message_only_under_Deferred()
    {
        await using var db = CreateContext();
        await AddAsync(db, "late@example.com", DeliveryOutcome.Deferred);
        await AddAsync(db, "waiting@example.com");

        var deferred = await ListAsync(db, status: SentMessageActivityStatus.Deferred);
        var sent = await ListAsync(db, status: SentMessageActivityStatus.Sent);

        var row = Assert.Single(deferred.Items);
        Assert.Equal(SentMessageActivityStatus.Deferred, row.Status);
        Assert.Equal("late@example.com", Assert.Single(row.Recipients));
        Assert.Equal("waiting@example.com", Assert.Single(Assert.Single(sent.Items).Recipients));
    }

    [Theory]
    [InlineData(SentMessageActivityStatus.Delivered)]
    [InlineData(SentMessageActivityStatus.Bounced)]
    [InlineData(SentMessageActivityStatus.Sent)]
    [InlineData(SentMessageActivityStatus.Deferred)]
    [InlineData(SentMessageActivityStatus.Complained)]
    [InlineData(SentMessageActivityStatus.Sending)]
    [InlineData(SentMessageActivityStatus.Suppressed)]
    [InlineData(SentMessageActivityStatus.Failed)]
    public async Task List_every_row_under_a_tab_shows_that_tab(string status)
    {
        await using var db = CreateContext();
        await AddAsync(db, "a@example.com", DeliveryOutcome.Delivered);
        await AddAsync(db, "b@example.com", DeliveryOutcome.HardBounced);
        await AddAsync(db, "c@example.com");
        await AddAsync(db, "d@example.com", DeliveryOutcome.Deferred);
        await AddAsync(db, "e@example.com", DeliveryOutcome.Complained);
        await AddTwoRecipientsAsync(db);
        await AddUnsentAsync(db, "queued@example.com", m => { });
        await AddUnsentAsync(db, "blocked@example.com", m => m.MarkAsSuppressed("on the list", T0));
        await AddUnsentAsync(
            db,
            "failed@example.com",
            m =>
            {
                m.MarkAsSending();
                m.MarkAsFailed("smtp down", T0);
            }
        );

        var result = await ListAsync(db, status: status);

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, row => Assert.Equal(status, row.Status));
    }

    [Theory]
    [InlineData(SentMessageActivityStatus.Sending, "queued@example.com")]
    [InlineData(SentMessageActivityStatus.Suppressed, "blocked@example.com")]
    public async Task List_filters_messages_that_never_left(string status, string expectedRecipient)
    {
        await using var db = CreateContext();
        await AddAsync(db, "sent@example.com");
        await AddUnsentAsync(db, "queued@example.com", m => { });
        await AddUnsentAsync(db, "blocked@example.com", m => m.MarkAsSuppressed("on the list", T0));

        var result = await ListAsync(db, status: status);

        Assert.Equal(expectedRecipient, Assert.Single(Assert.Single(result.Items).Recipients));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("3")]
    [InlineData("Accepted")]
    public void Filter_rejects_an_unknown_status_instead_of_showing_everything(string status)
    {
        var filter = SentMessageActivityFilter.Create(status, null, null, null);

        Assert.Equal("SentMessageActivityFilter.InvalidStatus", filter.Error.Code);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("deferred", SentMessageActivity.Deferred)]
    public void Filter_accepts_no_status_or_a_known_one(string? status, SentMessageActivity? expected)
    {
        Assert.Equal(expected, SentMessageActivityFilter.Create(status, null, null, null).Value.Status);
    }

    private static async Task AddUnsentAsync(PostmasterDbContext db, string recipient, Action<SentMessage> transition)
    {
        var message = SentMessage
            .Queue(
                TenantId,
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
                T0
            )
            .Value;
        message.AddRecipient(recipient, RecipientType.To, null);
        transition(message);
        await db.SentMessages.AddAsync(message);
        await db.SaveChangesAsync();
    }
}
