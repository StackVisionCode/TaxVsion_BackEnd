using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Application.Compliance.Reminders;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>Paso del job del WISP por oficina: el agregado decide, se guarda y recién después se publica.</summary>
public sealed class WispReviewRemindersTests
{
    private static readonly DateTime SavedAt = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _coordinatorId = Guid.NewGuid();
    private readonly InMemoryOfficeComplianceRepository _compliances = new();
    private readonly RecordingMessageBus _bus = new();
    private readonly PublishOrderUnitOfWork _unitOfWork;

    public WispReviewRemindersTests() => _unitOfWork = new PublishOrderUnitOfWork(_bus);

    [Fact]
    public async Task A_due_reminder_is_saved_first_and_then_published_with_the_coordinator()
    {
        Seed("2025-12-01");

        var reminder = await SendAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(WispReviewReminderStage.DueSoon, reminder!.Stage);
        Assert.Equal([0], _unitOfWork.PublishedAtSave);
        var evt = Assert.Single(_bus.Published.OfType<OfficeWispReviewDueIntegrationEvent>());
        Assert.Equal(_tenantId, evt.OfficeTenantId);
        Assert.Equal(_tenantId, evt.TenantId);
        Assert.Equal(new DateOnly(2026, 12, 1), evt.DueOn);
        Assert.Equal(OfficeWispReviewStages.DueSoon, evt.Stage);
        Assert.Equal(_coordinatorId, evt.SecurityCoordinatorUserId);
        Assert.Equal("sweep-correlation", evt.CorrelationId);
    }

    [Fact]
    public async Task Running_twice_the_same_day_publishes_once()
    {
        Seed("2025-12-01");
        var now = new DateTime(2026, 12, 2, 12, 0, 0, DateTimeKind.Utc);

        await SendAsync(now);
        var second = await SendAsync(now.AddHours(6));

        Assert.Null(second);
        Assert.Equal(
            OfficeWispReviewStages.Overdue,
            Assert.Single(_bus.Published.OfType<OfficeWispReviewDueIntegrationEvent>()).Stage
        );
        Assert.Single(_unitOfWork.PublishedAtSave);
    }

    [Fact]
    public async Task Nothing_due_saves_and_publishes_nothing()
    {
        Seed("2026-06-01");

        Assert.Null(await SendAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Null(
            await WispReviewReminders.SendIfDueAsync(
                new WispReminderCandidate(Guid.NewGuid(), "Etc/UTC"),
                SavedAt,
                "sweep-correlation",
                _compliances,
                _unitOfWork,
                _bus,
                CancellationToken.None
            )
        );

        Assert.Empty(_unitOfWork.PublishedAtSave);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_conflicting_save_publishes_nothing()
    {
        Seed("2025-12-01");
        _unitOfWork.ConflictOnSave.Add(1);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            SendAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc))
        );

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task The_stage_follows_the_office_calendar()
    {
        Seed("2025-12-01");
        var now = new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc);

        var reminder = await SendAsync(now, "America/Los_Angeles");

        Assert.Equal(WispReviewReminderStage.DueSoon, reminder!.Stage);
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

    private Task<WispReviewReminder?> SendAsync(DateTime nowUtc, string timeZoneId = "Etc/UTC") =>
        WispReviewReminders.SendIfDueAsync(
            new WispReminderCandidate(_tenantId, timeZoneId),
            nowUtc,
            "sweep-correlation",
            _compliances,
            _unitOfWork,
            _bus,
            CancellationToken.None
        );
}
