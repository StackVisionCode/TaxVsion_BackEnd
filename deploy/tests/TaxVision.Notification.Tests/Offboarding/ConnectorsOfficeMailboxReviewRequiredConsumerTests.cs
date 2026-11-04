using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Offboarding;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Offboarding;

/// <summary>Buzón de oficina ligado a un retirado: aviso a la audiencia del evento.</summary>
public sealed class ConnectorsOfficeMailboxReviewRequiredConsumerTests
{
    private static readonly Guid TenantId = OffboardingNoticeHarness.TenantId;
    private static readonly Guid Leaver = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid MailAdmin = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CoAdmin = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Avisa_a_la_audiencia_del_evento_con_destino_al_buzon()
    {
        var harness = Harness(host: "manfer.taxoffix.io");

        await HandleAsync(harness, Event("AddressOfOffboardedUser"));

        Assert.Equal(new[] { MailAdmin, CoAdmin }, harness.InAppRecipients.OrderBy(x => x));
        Assert.Equal(
            "connectors.accounts.write",
            Assert.Single(harness.Resolver.Audiences.OfType<ByPermission>()).PermissionCode
        );

        var row = harness.InAppRows[0];
        Assert.Equal(NotificationKinds.ConnectorsOfficeMailboxReviewRequired, row.Kind);
        Assert.Equal("mailbox_settings", row.ResourceType);
        Assert.Equal(AccountId.ToString(), row.ResourceId);
        Assert.Equal("Reconnect the office mailbox front@office.test", row.FallbackTitle);
        Assert.Contains("is the address of someone who's no longer with your office", row.FallbackBody);
        Assert.Equal($"connectors.office_mailbox_review_required:{AccountId:N}", row.GroupKey);

        var render = Assert.Single(harness.Scribe.Calls);
        Assert.Equal(ConnectorsOfficeMailboxReviewRequiredConsumer.EmailEventKey, render.EventKey);
        Assert.Equal("front@office.test", render.Variables["mailbox_email"]);
        Assert.Equal($"https://manfer.taxoffix.io/email?accountId={AccountId:D}", render.Variables["mailbox_url"]);
        Assert.Equal(2, harness.Postmaster.Count);
    }

    [Fact]
    public async Task Conectado_por_el_retirado_lo_dice()
    {
        var harness = Harness();

        await HandleAsync(harness, Event("ConnectedByOffboardedUser"));

        Assert.Contains("was connected by someone", harness.InAppRows[0].FallbackBody);
    }

    [Fact]
    public async Task Un_reintento_no_repite_correo_ni_push()
    {
        var harness = Harness();
        var evt = Event("AddressOfOffboardedUser");

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt);

        Assert.Equal(2, harness.Postmaster.Count);
        Assert.Equal(2, harness.Pushes.Count);
        Assert.Single(harness.InAppRows.Select(row => row.GroupKey).Distinct());
    }

    [Fact]
    public async Task Republicado_con_otro_EventId_no_repite_correo_ni_push()
    {
        var harness = Harness();
        var evt = Event("AddressOfOffboardedUser");

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt with { EventId = Guid.NewGuid() });

        Assert.Equal(2, harness.Postmaster.Count);
        Assert.Equal(2, harness.Pushes.Count);
    }

    [Fact]
    public async Task Otro_retiro_sobre_el_mismo_buzon_si_avisa()
    {
        var harness = Harness();
        var evt = Event("ConnectedByOffboardedUser");

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt with { EventId = Guid.NewGuid(), OffboardedUserId = Guid.NewGuid() });

        Assert.Equal(4, harness.Postmaster.Count);
        Assert.Equal(4, harness.Pushes.Count);
    }

    [Fact]
    public async Task Sin_nadie_con_el_permiso_no_avisa()
    {
        var harness = new OffboardingNoticeHarness(active: [MailAdmin]);

        await HandleAsync(harness, Event("AddressOfOffboardedUser"));

        Assert.Empty(harness.InAppRows);
        Assert.Empty(harness.Scribe.Calls);
    }

    private static OffboardingNoticeHarness Harness(string? host = null) =>
        new(
            active: [MailAdmin, CoAdmin],
            byPermission: new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["connectors.accounts.write"] = [MailAdmin, CoAdmin, Leaver],
            },
            host: host
        );

    private static ConnectorsOfficeMailboxReviewRequiredIntegrationEvent Event(string reason) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-mailbox",
            AccountId = AccountId,
            EmailAddress = "front@office.test",
            ProviderCode = "Graph",
            Reason = reason,
            OffboardedUserId = Leaver,
            AudiencePermission = "connectors.accounts.write",
            FlaggedAtUtc = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc),
        };

    private static Task HandleAsync(
        OffboardingNoticeHarness harness,
        ConnectorsOfficeMailboxReviewRequiredIntegrationEvent evt
    ) =>
        ConnectorsOfficeMailboxReviewRequiredConsumer.Handle(
            evt,
            harness.Sender,
            harness.Correlation,
            NullLogger<ConnectorsOfficeMailboxReviewRequiredIntegrationEvent>.Instance,
            CancellationToken.None
        );
}
