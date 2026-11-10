using TaxVision.Calendar.Application.Availability;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Availability.Commands;
using TaxVision.Calendar.Application.Availability.Queries;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Office;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

public sealed class AvailabilityManagementTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();
    private const string NewYork = "America/New_York";

    private static readonly AvailabilityRuleInput Weekdays9To17 = new(
        (int)DaysOfWeekMask.Weekdays,
        new TimeOnly(9, 0),
        new TimeOnly(17, 0),
        NewYork
    );

    [Fact]
    public async Task A_preparer_replaces_their_whole_weekly_schedule()
    {
        var old = AvailabilityRule
            .Create(Tenant, Me, DaysOfWeekMask.Saturday, new(8, 0), new(12, 0), NewYork, DateTime.UtcNow)
            .Value;
        var store = new RulesAndBlocks([old]);
        var unitOfWork = new CountingUnitOfWork();

        var result = await ReplaceAvailabilityRulesHandler.Handle(
            new ReplaceAvailabilityRulesCommand(Tenant, Me, Me, CanManageOthers: false, [Weekdays9To17]),
            store,
            unitOfWork,
            CancellationToken.None
        );

        var rule = Assert.Single(result.Value);
        Assert.Equal((int)DaysOfWeekMask.Weekdays, rule.DaysMask);
        Assert.False(old.IsActive);
        Assert.Single(store.Rules, r => r.IsActive);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Someone_elses_schedule_needs_the_availability_permission()
    {
        var store = new RulesAndBlocks([]);

        var denied = await ReplaceAvailabilityRulesHandler.Handle(
            new ReplaceAvailabilityRulesCommand(Tenant, Me, Colleague, CanManageOthers: false, [Weekdays9To17]),
            store,
            new CountingUnitOfWork(),
            CancellationToken.None
        );
        Assert.Equal(AvailabilityErrors.Forbidden, denied.Error);
        Assert.Empty(store.Rules);

        var allowed = await ReplaceAvailabilityRulesHandler.Handle(
            new ReplaceAvailabilityRulesCommand(Tenant, Me, Colleague, CanManageOthers: true, [Weekdays9To17]),
            store,
            new CountingUnitOfWork(),
            CancellationToken.None
        );
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public async Task One_bad_rule_rejects_the_whole_schedule()
    {
        var old = AvailabilityRule
            .Create(Tenant, Me, DaysOfWeekMask.Monday, new(8, 0), new(12, 0), NewYork, DateTime.UtcNow)
            .Value;
        var store = new RulesAndBlocks([old]);

        var result = await ReplaceAvailabilityRulesHandler.Handle(
            new ReplaceAvailabilityRulesCommand(
                Tenant,
                Me,
                Me,
                false,
                [Weekdays9To17, new AvailabilityRuleInput((int)DaysOfWeekMask.Friday, new(17, 0), new(9, 0), NewYork)]
            ),
            store,
            new CountingUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(AvailabilityErrors.EndBeforeStart, result.Error);
        Assert.True(old.IsActive);
    }

    [Fact]
    public async Task Time_off_is_blocked_and_only_its_owner_or_a_manager_removes_it()
    {
        var store = new RulesAndBlocks([]);
        var start = new DateTime(2026, 11, 26, 5, 0, 0, DateTimeKind.Utc);

        var created = await CreateBlockedTimeHandler.Handle(
            new CreateBlockedTimeCommand(Tenant, Me, Me, false, start, start.AddDays(1), "Thanksgiving"),
            store,
            new CountingUnitOfWork(),
            CancellationToken.None
        );
        Assert.Equal("Thanksgiving", created.Value.Reason);

        var denied = await RemoveBlockedTimeHandler.Handle(
            new RemoveBlockedTimeCommand(Tenant, Colleague, created.Value.Id, CanManageOthers: false),
            store,
            new CountingUnitOfWork(),
            CancellationToken.None
        );
        Assert.Equal(AvailabilityErrors.Forbidden, denied.Error);

        var removed = await RemoveBlockedTimeHandler.Handle(
            new RemoveBlockedTimeCommand(Tenant, Me, created.Value.Id, CanManageOthers: false),
            store,
            new CountingUnitOfWork(),
            CancellationToken.None
        );
        Assert.True(removed.IsSuccess);
        Assert.Empty(store.Blocks);
    }

    [Fact]
    public async Task Free_slots_stay_inside_office_hours_and_skip_office_closures()
    {
        // El preparador trabaja 9–17; la oficina abre 10–14 y cierra el jueves 26 (Thanksgiving).
        var rule = AvailabilityRule
            .Create(Tenant, Me, DaysOfWeekMask.Weekdays, new(9, 0), new(17, 0), NewYork, DateTime.UtcNow)
            .Value;
        var office = OfficeScheduleProjection.Create(Tenant, DateTime.UtcNow);
        office.ApplyTimeZone(NewYork, DateTime.UtcNow, DateTime.UtcNow);
        office.ApplyProfile(
            1,
            [
                new OfficeHours(DayOfWeek.Wednesday, new(10, 0), new(14, 0)),
                new OfficeHours(DayOfWeek.Thursday, new(10, 0), new(14, 0)),
            ],
            [new OfficeClosureSpan(new DateOnly(2026, 11, 26), null, "Thanksgiving", false)],
            DateTime.UtcNow
        );

        var result = await GetAvailabilityHandler.Handle(
            new GetAvailabilityQuery(
                Tenant,
                Me,
                new DateTime(2026, 11, 25, 5, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 27, 5, 0, 0, DateTimeKind.Utc),
                null
            ),
            new InMemoryAppointments(),
            new RulesAndBlocks([rule]),
            new SingleTypeRepository(null!),
            new OneOffice(office),
            CancellationToken.None
        );

        var slot = Assert.Single(result.Value);
        Assert.Equal(new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc), slot.StartUtc); // 10:00 EST
        Assert.Equal(new DateTime(2026, 11, 25, 19, 0, 0, DateTimeKind.Utc), slot.EndUtc); // 14:00 EST
    }

    private sealed class OneOffice(OfficeScheduleProjection office) : IOfficeScheduleRepository
    {
        public Task<OfficeScheduleProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<OfficeScheduleProjection?>(office);

        public void Add(OfficeScheduleProjection projection) { }
    }

    private sealed class RulesAndBlocks(IEnumerable<AvailabilityRule> rules) : IAvailabilityRepository
    {
        public List<AvailabilityRule> Rules { get; } = rules.ToList();
        public List<BlockedTime> Blocks { get; } = [];

        public Task<IReadOnlyList<AvailabilityRule>> ListRulesAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<AvailabilityRule>>(
                Rules.Where(r => r.UserId == userId && r.IsActive).ToList()
            );

        public Task<IReadOnlyList<BlockedTime>> ListBlocksAsync(
            Guid tenantId,
            Guid userId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<BlockedTime>>(
                Blocks.Where(b => b.UserId == userId && b.Overlaps(fromUtc, toUtc)).ToList()
            );

        public Task<IReadOnlyList<BlockedTime>> ListAllBlocksForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<BlockedTime>>(Blocks.Where(b => b.UserId == userId).ToList());

        public Task<BlockedTime?> FindBlockAsync(Guid tenantId, Guid blockId, CancellationToken ct = default) =>
            Task.FromResult(Blocks.FirstOrDefault(b => b.Id == blockId));

        public void AddRule(AvailabilityRule rule) => Rules.Add(rule);

        public void AddBlock(BlockedTime block) => Blocks.Add(block);

        public void RemoveBlock(BlockedTime block) => Blocks.Remove(block);
    }
}
