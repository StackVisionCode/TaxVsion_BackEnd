using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Offboarding;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Offboarding;

/// <summary>Retiro sin todos los acuses en plazo: aviso a quienes gestionan usuarios, con las áreas pendientes.</summary>
public sealed class UserOffboardingIncompleteConsumerTests
{
    private static readonly Guid TenantId = OffboardingNoticeHarness.TenantId;
    private static readonly Guid Leaver = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Admin = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CoAdmin = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public async Task Avisa_a_users_manage_con_las_areas_en_palabras_de_la_oficina()
    {
        var harness = Harness(host: "manfer.taxoffix.io");

        await HandleAsync(harness, Event());

        Assert.Equal(new[] { Admin, CoAdmin }, harness.InAppRecipients.OrderBy(x => x));
        Assert.Equal("users.manage", Assert.Single(harness.Resolver.Audiences.OfType<ByPermission>()).PermissionCode);

        var row = harness.InAppRows[0];
        Assert.Equal(NotificationKinds.AuthOffboardingIncomplete, row.Kind);
        Assert.Equal("user_management", row.ResourceType);
        Assert.Equal(Leaver.ToString(), row.ResourceId);
        Assert.Equal("Andres Hernandez's offboarding didn't finish", row.FallbackTitle);
        Assert.Contains(
            "these areas haven't confirmed the handover yet: Calendar, Chat and meetings",
            row.FallbackBody
        );
        // Nunca nombres internos (servicio o consumer).
        Assert.DoesNotContain("Consumer", row.FallbackBody);
        Assert.DoesNotContain("offboarding-consumers", row.FallbackBody);
        Assert.Equal($"auth.offboarding_incomplete:{Leaver:N}", row.GroupKey);

        var render = Assert.Single(harness.Scribe.Calls);
        Assert.Equal(UserOffboardingIncompleteConsumer.EmailEventKey, render.EventKey);
        Assert.Equal("Andres Hernandez", render.Variables["user_name"]);
        Assert.Equal(new[] { "Calendar", "Chat and meetings" }, (string[])render.Variables["pending_modules"]!);
        Assert.Equal($"https://manfer.taxoffix.io/company/users?handover={Leaver}", render.Variables["users_url"]);
        Assert.Equal(2, harness.Postmaster.Count);
    }

    [Fact]
    public async Task Republicado_con_otro_EventId_no_repite_correo_ni_push()
    {
        var harness = Harness();
        var evt = Event();

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt with { EventId = Guid.NewGuid() });

        Assert.Equal(2, harness.Postmaster.Count);
        Assert.Equal(2, harness.Pushes.Count);
        Assert.Single(harness.InAppRows.Select(row => row.GroupKey).Distinct());
    }

    [Fact]
    public async Task Sin_pasos_pendientes_o_sin_destinatarios_no_avisa()
    {
        var harness = Harness();
        await HandleAsync(harness, Event() with { PendingSteps = [] });
        Assert.Empty(harness.InAppRows);

        var nobody = new OffboardingNoticeHarness(active: [Admin]);
        await HandleAsync(nobody, Event());
        Assert.Empty(nobody.InAppRows);
        Assert.Empty(nobody.Scribe.Calls);
    }

    private static OffboardingNoticeHarness Harness(string? host = null) =>
        new(
            active: [Admin, CoAdmin],
            byPermission: new Dictionary<string, IReadOnlyList<Guid>> { ["users.manage"] = [Admin, CoAdmin, Leaver] },
            host: host
        );

    private static UserOffboardingIncompleteIntegrationEvent Event() =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-offboarding",
            UserId = Leaver,
            UserDisplayName = "Andres Hernandez",
            StartedAtUtc = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc),
            ExpectedSteps = 48,
            CompletedSteps = 45,
            PendingSteps =
            [
                new("Calendar", "CalendarUserOffboardedConsumer", "Calendar"),
                new("Calendar", "UserLifecyclePermissionsProjectionConsumer", "Calendar"),
                new("Communication", "offboarding-consumers", "Chat and meetings"),
            ],
        };

    private static Task HandleAsync(OffboardingNoticeHarness harness, UserOffboardingIncompleteIntegrationEvent evt) =>
        UserOffboardingIncompleteConsumer.Handle(
            evt,
            harness.Sender,
            harness.Correlation,
            NullLogger<UserOffboardingIncompleteIntegrationEvent>.Instance,
            CancellationToken.None
        );
}
