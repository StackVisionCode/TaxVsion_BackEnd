using BuildingBlocks.Messaging.TenantIntegrationEvents;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Application.Compliance.Reminders;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>Paso del job del WISP por oficina: el agregado decide y anota, y se devuelve el aviso a publicar.</summary>
public sealed class WispReviewRemindersTests
{
    private static readonly DateTime SavedAt = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _coordinatorId = Guid.NewGuid();
    private readonly InMemoryOfficeComplianceRepository _compliances = new();

    [Fact]
    public async Task Due_ReturnsEvent()
    {
        Seed("2025-12-01");

        var evt = await RegisterAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(evt);
        Assert.Equal(_tenantId, evt.OfficeTenantId);
        Assert.Equal(_tenantId, evt.TenantId);
        Assert.Equal(new DateOnly(2026, 12, 1), evt.DueOn);
        Assert.Equal(OfficeWispReviewStages.DueSoon, evt.Stage);
        Assert.Equal(_coordinatorId, evt.SecurityCoordinatorUserId);
        Assert.Equal("sweep-correlation", evt.CorrelationId);
        Assert.Equal(new DateOnly(2026, 11, 10), _compliances.Compliances[0].WispReminderSentOn);
    }

    [Fact]
    public async Task Running_twice_the_same_day_returns_the_event_once()
    {
        Seed("2025-12-01");
        var now = new DateTime(2026, 12, 2, 12, 0, 0, DateTimeKind.Utc);

        var first = await RegisterAsync(now);
        var second = await RegisterAsync(now.AddHours(6));

        Assert.Equal(OfficeWispReviewStages.Overdue, first!.Stage);
        Assert.Null(second);
    }

    [Fact]
    public async Task NotDue_ReturnsNull()
    {
        Seed("2026-06-01");

        Assert.Null(await RegisterAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Null(
            await WispReviewReminders.RegisterIfDueAsync(
                new WispReminderCandidate(Guid.NewGuid(), "Etc/UTC"),
                SavedAt,
                "sweep-correlation",
                _compliances,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task The_stage_follows_the_office_calendar()
    {
        Seed("2025-12-01");
        var now = new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc);

        var evt = await RegisterAsync(now, "America/Los_Angeles");

        Assert.Equal(OfficeWispReviewStages.DueSoon, evt!.Stage);
    }

    [Fact]
    public void The_coarse_filter_never_leaves_out_an_office_in_its_due_soon_window()
    {
        var now = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);
        var latestBase = WispReviewReminders.LatestCandidateBase(now);

        // En UTC+14 ya es 3-nov; vence el 3-dic → base 3-dic-2025 tiene que entrar.
        Assert.True(new DateOnly(2025, 12, 3) <= latestBase);
        // 29-feb: vence el 28-feb del año siguiente y entra el primer día de su ventana.
        Assert.True(
            new DateOnly(2024, 2, 29)
                <= WispReviewReminders.LatestCandidateBase(new DateTime(2025, 1, 29, 0, 0, 0, DateTimeKind.Utc))
        );
    }

    private void Seed(string adoptedOn)
    {
        var compliance = OfficeCompliance.CreateEmpty(_tenantId);
        compliance.Update(
            new OfficeComplianceDraft(_coordinatorId, adoptedOn, null, null, null, null),
            EfinChange.Keep,
            Guid.NewGuid(),
            SavedAt
        );
        _compliances.Compliances.Add(compliance);
    }

    private Task<OfficeWispReviewDueIntegrationEvent?> RegisterAsync(DateTime nowUtc, string timeZoneId = "Etc/UTC") =>
        WispReviewReminders.RegisterIfDueAsync(
            new WispReminderCandidate(_tenantId, timeZoneId),
            nowUtc,
            "sweep-correlation",
            _compliances,
            CancellationToken.None
        );
}
