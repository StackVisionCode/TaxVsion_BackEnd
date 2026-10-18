using TaxVision.Auth.Application.SlotGating;

namespace TaxVision.Auth.Tests.SlotGating;

public sealed class TenantSeatSlotCalculatorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Owner_is_first_and_allowed_with_cap_one()
    {
        var owner = Candidate(Guid.NewGuid(), true, DateTime.MinValue);

        var rows = Calculate([Candidate(Guid.NewGuid(), false, Now), owner], 1);

        Assert.Equal(owner.UserId, rows[0].UserId);
        Assert.False(rows[0].IsGated);
        Assert.True(rows[1].IsGated);
    }

    [Fact]
    public void Newest_employee_is_gated_when_cap_is_lower_than_active_staff()
    {
        var owner = Candidate(Guid.NewGuid(), true, DateTime.MinValue);
        var older = Candidate(Guid.NewGuid(), false, Now.AddDays(-2));
        var newer = Candidate(Guid.NewGuid(), false, Now.AddDays(-1));

        var rows = Calculate([newer, owner, older], 2);

        Assert.Equal([owner.UserId, older.UserId, newer.UserId], rows.Select(row => row.UserId));
        Assert.False(rows[1].IsGated);
        Assert.True(rows[2].IsGated);
    }

    [Fact]
    public void Equal_priority_uses_user_id_as_stable_tiebreaker()
    {
        var low = Candidate(Guid.Parse("00000000-0000-0000-0000-000000000001"), false, Now);
        var high = Candidate(Guid.Parse("00000000-0000-0000-0000-000000000002"), false, Now);

        var rows = Calculate([high, low], 2);

        Assert.Equal([low.UserId, high.UserId], rows.Select(row => row.UserId));
    }

    [Fact]
    public void Null_priority_is_ordered_last()
    {
        var assigned = Candidate(Guid.NewGuid(), false, Now);
        var missing = Candidate(Guid.NewGuid(), false, null);

        var rows = Calculate([missing, assigned], 2);

        Assert.Equal(assigned.UserId, rows[0].UserId);
        Assert.Equal(missing.UserId, rows[1].UserId);
    }

    [Fact]
    public void Cap_zero_gates_everyone_except_owner()
    {
        var owner = Candidate(Guid.NewGuid(), true, DateTime.MinValue);
        var employee = Candidate(Guid.NewGuid(), false, Now);

        var rows = Calculate([employee, owner], 0);

        Assert.False(rows.Single(row => row.UserId == owner.UserId).IsGated);
        Assert.True(rows.Single(row => row.UserId == employee.UserId).IsGated);
    }

    [Fact]
    public void Cap_above_active_count_allows_all_users()
    {
        var rows = Calculate(
            [Candidate(Guid.NewGuid(), false, Now), Candidate(Guid.NewGuid(), false, Now.AddMinutes(1))],
            10
        );

        Assert.All(rows, row => Assert.False(row.IsGated));
    }

    private static IReadOnlyList<TaxVision.Auth.Application.SlotGating.Abstractions.TenantSeatSlotProjectionRecord> Calculate(
        IReadOnlyCollection<TenantSeatSlotCandidate> candidates,
        int cap
    ) => TenantSeatSlotCalculator.Calculate(TenantId, candidates, cap, Now, 1);

    private static TenantSeatSlotCandidate Candidate(Guid id, bool owner, DateTime? priority) =>
        new(id, owner, priority);
}
