using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Offboarding;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Offboarding;

/// <summary>Agendados de campañas pausados por retiro: aviso a <c>campaigns.manage</c> ∪ <c>campaigns.send</c>.</summary>
public sealed class CampaignSchedulesPausedOnOffboardingConsumerTests
{
    private static readonly Guid TenantId = OffboardingNoticeHarness.TenantId;
    private static readonly Guid Leaver = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Manager = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Sender = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Both = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid CampaignA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid CampaignB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Avisa_a_quien_administra_o_envia_campañas_sin_repetir()
    {
        var harness = Harness();

        await HandleAsync(harness, Event(("Spring promo", CampaignA), ("Tax tips", CampaignB)));

        Assert.Equal(new[] { Manager, Sender, Both }.OrderBy(x => x), harness.InAppRecipients.OrderBy(x => x));
        Assert.Equal(
            new[] { "campaigns.manage", "campaigns.send" },
            harness.Resolver.Audiences.OfType<ByPermission>().Select(a => a.PermissionCode)
        );
        Assert.Equal(3, harness.Postmaster.Count);
        Assert.Equal(3, harness.Pushes.Count);
    }

    [Fact]
    public async Task Lista_las_campañas_y_pide_cancelarlas_o_volver_a_agendarlas()
    {
        var harness = Harness(host: "manfer.taxoffix.io");
        // Dos agendados de la misma campaña cuentan como una.
        var evt = Event(("Spring promo", CampaignA), ("Spring promo", CampaignA), ("Tax tips", CampaignB));

        await HandleAsync(harness, evt);

        var row = harness.InAppRows[0];
        Assert.Equal(NotificationKinds.CampaignsSchedulesPausedOnOffboarding, row.Kind);
        Assert.Equal("campaign", row.ResourceType);
        Assert.Equal("2 scheduled campaigns were paused", row.FallbackTitle);
        Assert.Contains("Spring promo, Tax tips", row.FallbackBody);
        Assert.Contains("no longer with your office", row.FallbackBody);
        Assert.Contains("Cancel them or schedule them again", row.FallbackBody);
        Assert.Null(row.ResourceId);
        Assert.Equal("3", row.Params["scheduleCount"]);
        Assert.Equal($"campaigns.schedules_paused_on_offboarding:{Leaver:N}", row.GroupKey);

        var render = Assert.Single(harness.Scribe.Calls);
        Assert.Equal(CampaignSchedulesPausedOnOffboardingConsumer.EmailEventKey, render.EventKey);
        Assert.Equal(2, render.Variables["campaign_count"]);
        Assert.Equal(new[] { "Spring promo", "Tax tips" }, (string[])render.Variables["campaign_names"]!);
        Assert.Equal("https://manfer.taxoffix.io/campaigns", render.Variables["campaigns_url"]);
    }

    [Fact]
    public async Task Una_sola_campaña_habla_en_singular_y_apunta_a_ella()
    {
        var harness = Harness();

        await HandleAsync(harness, Event(("Spring promo", CampaignA)));

        var row = harness.InAppRows[0];
        Assert.Equal("A scheduled campaign was paused", row.FallbackTitle);
        Assert.Contains("Cancel it or schedule it again", row.FallbackBody);
        Assert.Equal(CampaignA.ToString(), row.ResourceId);
    }

    [Fact]
    public async Task Respeta_la_preferencia_de_correo_pero_llega_la_campana()
    {
        var harness = Harness(emailDisabled: [Manager]);

        await HandleAsync(harness, Event(("Spring promo", CampaignA)));

        Assert.DoesNotContain(OffboardingNoticeHarness.EmailOf(Manager), harness.Postmaster.Select(e => e.To));
        Assert.Contains(Manager, harness.InAppRecipients);
        Assert.False(
            NotificationCatalog
                .GetRequired(NotificationKinds.CampaignsSchedulesPausedOnOffboarding)
                .IsMandatoryFor(NotificationChannel.Email)
        );
    }

    [Fact]
    public async Task Staff_inactivo_en_el_directorio_no_recibe_nada()
    {
        var harness = Harness(inactiveInDirectory: [Sender]);

        await HandleAsync(harness, Event(("Spring promo", CampaignA)));

        Assert.DoesNotContain(Sender, harness.InAppRecipients);
        Assert.DoesNotContain(OffboardingNoticeHarness.EmailOf(Sender), harness.Postmaster.Select(e => e.To));
        Assert.Equal(2, harness.Pushes.Count);
    }

    [Fact]
    public async Task Sin_agendados_no_avisa()
    {
        var harness = Harness();

        await HandleAsync(harness, Event());

        Assert.Empty(harness.Resolver.Audiences);
        Assert.Empty(harness.InAppRows);
    }

    [Fact]
    public async Task Los_mismos_agendados_republicados_no_repiten_correo_ni_push()
    {
        var harness = Harness();
        var evt = Event(("Spring promo", CampaignA));

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt with { EventId = Guid.NewGuid() });

        Assert.Equal(3, harness.Postmaster.Count);
        Assert.Equal(3, harness.Pushes.Count);
    }

    [Fact]
    public async Task Otros_agendados_pausados_si_avisan()
    {
        var harness = Harness();

        await HandleAsync(harness, Event(("Spring promo", CampaignA)));
        await HandleAsync(harness, Event(("Spring promo", CampaignA)));

        Assert.Equal(6, harness.Postmaster.Count);
        Assert.Equal(6, harness.Pushes.Count);
    }

    private static OffboardingNoticeHarness Harness(
        string? host = null,
        IReadOnlyCollection<Guid>? emailDisabled = null,
        IReadOnlyCollection<Guid>? inactiveInDirectory = null
    ) =>
        new(
            active: [Manager, Sender, Both],
            byPermission: new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["campaigns.manage"] = [Manager, Both],
                ["campaigns.send"] = [Sender, Both, Leaver],
            },
            host: host,
            emailDisabled: emailDisabled,
            inactiveInDirectory: inactiveInDirectory
        );

    private static CampaignSchedulesPausedOnOffboardingIntegrationEvent Event(
        params (string Name, Guid CampaignId)[] schedules
    ) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-campaigns",
            OffboardedUserId = Leaver,
            OffboardedByUserId = Manager,
            Schedules = schedules
                .Select(s => new PausedCampaignSchedule(Guid.NewGuid(), s.CampaignId, s.Name))
                .ToArray(),
            PausedAtUtc = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc),
        };

    private static Task HandleAsync(
        OffboardingNoticeHarness harness,
        CampaignSchedulesPausedOnOffboardingIntegrationEvent evt
    ) =>
        CampaignSchedulesPausedOnOffboardingConsumer.Handle(
            evt,
            harness.Sender,
            harness.Correlation,
            NullLogger<CampaignSchedulesPausedOnOffboardingIntegrationEvent>.Instance,
            CancellationToken.None
        );
}
