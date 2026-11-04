using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Offboarding;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Offboarding;

/// <summary>Coordinador de seguridad (WISP) vacante por retiro: aviso a <c>settings.manage</c>.</summary>
public sealed class OfficeSecurityCoordinatorVacatedConsumerTests
{
    private static readonly Guid TenantId = OffboardingNoticeHarness.TenantId;
    private static readonly Guid Former = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Admin = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CoAdmin = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public async Task Avisa_a_settings_manage_por_campana_correo_y_push_con_destino_a_Compliance()
    {
        var harness = Harness(host: "manfer.taxoffix.io");
        var evt = Event();

        await HandleAsync(harness, evt);

        Assert.Equal(new[] { Admin, CoAdmin }, harness.InAppRecipients.OrderBy(x => x));
        var byPermission = Assert.Single(harness.Resolver.Audiences.OfType<ByPermission>());
        Assert.Equal("settings.manage", byPermission.PermissionCode);

        var row = harness.InAppRows[0];
        Assert.Equal(NotificationKinds.TenantSecurityCoordinatorVacated, row.Kind);
        Assert.Equal("compliance_settings", row.ResourceType);
        Assert.Equal("compliance", row.ResourceContext["section"]);
        Assert.Equal("Your office needs a new security coordinator", row.FallbackTitle);
        Assert.Contains("no longer with your office", row.FallbackBody);
        Assert.Contains("Office settings → Compliance", row.FallbackBody);
        Assert.Equal($"tenant.security_coordinator_vacated:{TenantId:N}", row.GroupKey);

        var render = Assert.Single(harness.Scribe.Calls);
        Assert.Equal(OfficeSecurityCoordinatorVacatedConsumer.EmailEventKey, render.EventKey);
        Assert.Equal("https://manfer.taxoffix.io/company/settings#compliance", render.Variables["settings_url"]);
        Assert.Equal(2, harness.Postmaster.Count);
        Assert.All(harness.Queued, email => Assert.Equal(evt.EventId, email.RelatedEventId));
        Assert.Equal(2, harness.Pushes.Count);
        Assert.All(
            harness.Pushes,
            push => Assert.Equal("https://manfer.taxoffix.io/company/settings#compliance", push.Link)
        );
    }

    [Fact]
    public async Task Es_obligatorio_por_correo_aunque_el_admin_lo_haya_apagado()
    {
        var harness = Harness(emailDisabled: [Admin]);

        await HandleAsync(harness, Event());

        Assert.Contains(OffboardingNoticeHarness.EmailOf(Admin), harness.Postmaster.Select(email => email.To));
        var entry = NotificationCatalog.GetRequired(NotificationKinds.TenantSecurityCoordinatorVacated);
        Assert.True(entry.IsMandatoryFor(NotificationChannel.InApp));
        Assert.True(entry.IsMandatoryFor(NotificationChannel.Email));
        Assert.False(entry.IsMandatoryFor(NotificationChannel.Push));
    }

    [Fact]
    public async Task Un_reintento_no_repite_correo_ni_push()
    {
        var harness = Harness();
        var evt = Event();

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt);

        Assert.Equal(2, harness.Postmaster.Count);
        Assert.Equal(2, harness.Pushes.Count);
        Assert.Single(harness.InAppRows.Select(row => row.GroupKey).Distinct());
    }

    [Fact]
    public async Task Si_Scribe_falla_no_sale_nada_y_se_reintenta()
    {
        var harness = Harness(scribeFails: true);

        await Assert.ThrowsAsync<ScribeRenderFailedException>(() => HandleAsync(harness, Event()));

        Assert.Empty(harness.InAppRows);
        Assert.Empty(harness.Pushes);
    }

    [Fact]
    public async Task Un_motivo_desconocido_no_avisa()
    {
        var harness = Harness();

        await HandleAsync(harness, Event() with { Reason = "Later" });

        Assert.Empty(harness.Resolver.Audiences);
        Assert.Empty(harness.InAppRows);
    }

    private static OffboardingNoticeHarness Harness(
        string? host = null,
        bool scribeFails = false,
        IReadOnlyCollection<Guid>? emailDisabled = null
    ) =>
        new(
            active: [Admin, CoAdmin],
            byPermission: new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["settings.manage"] = [Admin, CoAdmin, Former],
            },
            host: host,
            scribeFails: scribeFails,
            emailDisabled: emailDisabled
        );

    private static OfficeSecurityCoordinatorVacatedIntegrationEvent Event() =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-coordinator",
            OfficeTenantId = TenantId,
            FormerCoordinatorUserId = Former,
            Reason = OfficeSecurityCoordinatorVacancyReasons.Offboarded,
            VacatedByUserId = Admin,
            VacatedAtUtc = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc),
        };

    private static Task HandleAsync(
        OffboardingNoticeHarness harness,
        OfficeSecurityCoordinatorVacatedIntegrationEvent evt
    ) =>
        OfficeSecurityCoordinatorVacatedConsumer.Handle(
            evt,
            harness.Sender,
            harness.Correlation,
            NullLogger<OfficeSecurityCoordinatorVacatedIntegrationEvent>.Instance,
            CancellationToken.None
        );
}
