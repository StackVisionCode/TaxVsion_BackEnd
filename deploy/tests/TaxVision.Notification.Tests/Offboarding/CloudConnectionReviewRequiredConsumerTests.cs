using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Offboarding;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Offboarding;

/// <summary>Conexión de nube de la oficina ligada a un retirado: aviso a la audiencia del evento.</summary>
public sealed class CloudConnectionReviewRequiredConsumerTests
{
    private static readonly Guid TenantId = OffboardingNoticeHarness.TenantId;
    private static readonly Guid Leaver = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid StorageAdmin = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Inactive = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid ConnectionId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Avisa_a_la_audiencia_del_evento_con_destino_a_Storage()
    {
        var harness = Harness(host: "manfer.taxoffix.io");

        await HandleAsync(harness, Event("AccountOfOffboardedUser", "google-drive"));

        Assert.Equal(new[] { StorageAdmin }, harness.InAppRecipients);
        Assert.Equal(
            "cloudstorage.settings.manage",
            Assert.Single(harness.Resolver.Audiences.OfType<ByPermission>()).PermissionCode
        );

        var row = harness.InAppRows[0];
        Assert.Equal(NotificationKinds.CloudStorageConnectionReviewRequired, row.Kind);
        Assert.Equal("storage_settings", row.ResourceType);
        Assert.Equal("reconnect", row.Action);
        Assert.Equal(ConnectionId.ToString(), row.ResourceId);
        Assert.Equal("Reconnect your office's Google Drive connection", row.FallbackTitle);
        Assert.Contains("uses the account docs@office.test", row.FallbackBody);
        Assert.Contains("Reconnect it with an account that belongs to your office", row.FallbackBody);
        Assert.Equal($"cloudstorage.connection_review_required:{ConnectionId:N}", row.GroupKey);

        var render = Assert.Single(harness.Scribe.Calls);
        Assert.Equal(CloudConnectionReviewRequiredConsumer.EmailEventKey, render.EventKey);
        Assert.Equal("Google Drive", render.Variables["provider_name"]);
        Assert.Equal("https://manfer.taxoffix.io/storage", render.Variables["storage_url"]);
        Assert.Equal("https://manfer.taxoffix.io/storage", Assert.Single(harness.Pushes).Link);
    }

    [Theory]
    [InlineData("ConnectedByOffboardedUser", "onedrive", "OneDrive", "was set up by someone")]
    [InlineData("SomethingElse", "dropbox", "Dropbox", "is linked to someone")]
    public async Task El_texto_depende_del_motivo_y_del_proveedor(
        string reason,
        string provider,
        string providerName,
        string bodyFragment
    )
    {
        var harness = Harness();

        await HandleAsync(harness, Event(reason, provider));

        var row = Assert.Single(harness.InAppRows);
        Assert.Contains(providerName, row.FallbackTitle);
        Assert.Contains(bodyFragment, row.FallbackBody);
    }

    [Fact]
    public async Task Staff_inactivo_y_retirado_quedan_fuera()
    {
        var harness = Harness(inactiveInDirectory: [Inactive], active: [StorageAdmin, Inactive]);

        await HandleAsync(harness, Event("AccountOfOffboardedUser", "google-drive"));

        Assert.Equal(new[] { StorageAdmin }, harness.InAppRecipients);
        Assert.Equal(new[] { OffboardingNoticeHarness.EmailOf(StorageAdmin) }, harness.Postmaster.Select(e => e.To));
    }

    [Fact]
    public async Task Republicado_con_otro_EventId_no_repite_correo_ni_push()
    {
        var harness = Harness();
        var evt = Event("AccountOfOffboardedUser", "google-drive");

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt with { EventId = Guid.NewGuid() });

        Assert.Single(harness.Postmaster);
        Assert.Single(harness.Pushes);
        Assert.Single(harness.InAppRows.Select(row => row.GroupKey).Distinct());
    }

    [Fact]
    public async Task Otro_retiro_sobre_la_misma_conexion_si_avisa()
    {
        var harness = Harness();
        var evt = Event("ConnectedByOffboardedUser", "google-drive");

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt with { EventId = Guid.NewGuid(), OffboardedUserId = Guid.NewGuid() });

        Assert.Equal(2, harness.Postmaster.Count);
        Assert.Equal(2, harness.Pushes.Count);
    }

    private static OffboardingNoticeHarness Harness(
        string? host = null,
        IReadOnlyCollection<Guid>? inactiveInDirectory = null,
        IReadOnlyList<Guid>? active = null
    ) =>
        new(
            active: active ?? [StorageAdmin],
            byPermission: new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["cloudstorage.settings.manage"] = [StorageAdmin, Leaver, Inactive],
            },
            host: host,
            inactiveInDirectory: inactiveInDirectory
        );

    private static CloudConnectionReviewRequiredIntegrationEvent Event(string reason, string provider) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-cloud",
            ConnectionId = ConnectionId,
            Provider = provider,
            AccountEmail = "docs@office.test",
            Reason = reason,
            OffboardedUserId = Leaver,
            AudiencePermission = "cloudstorage.settings.manage",
            FlaggedAtUtc = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc),
        };

    private static Task HandleAsync(
        OffboardingNoticeHarness harness,
        CloudConnectionReviewRequiredIntegrationEvent evt
    ) =>
        CloudConnectionReviewRequiredConsumer.Handle(
            evt,
            harness.Sender,
            harness.Correlation,
            NullLogger<CloudConnectionReviewRequiredIntegrationEvent>.Instance,
            CancellationToken.None
        );
}
