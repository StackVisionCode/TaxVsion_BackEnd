using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Migrations;

public enum SubscriberMigrationMode
{
    /// <summary>Se aplica ya. Solo si para todas las oficinas es pura mejora.</summary>
    Immediate,

    /// <summary>Se aplica en la primera renovación que cae después del aviso (mínimo 30 días).</summary>
    AtNextRenewal,
}

public enum SubscriberMigrationStatus
{
    Running,
    Completed,
    Cancelled,
}

public enum SubscriberMigrationOutcome
{
    Pending,
    Applied,
    Scheduled,
    Blocked,
    Skipped,
    Unscheduled,
}

/// <summary>Una oficina a mover y, si la vista previa la bloqueó, por qué.</summary>
public sealed record SubscriberMigrationOfficePlan(Guid TenantId, Guid SubscriptionId, string? BlockedReason);

/// <summary>Qué le pasó a una oficina en la migración.</summary>
public sealed class SubscriberMigrationOffice : BaseEntity
{
    private SubscriberMigrationOffice() { }

    internal SubscriberMigrationOffice(Guid migrationId, SubscriberMigrationOfficePlan plan)
    {
        MigrationId = migrationId;
        TenantId = plan.TenantId;
        SubscriptionId = plan.SubscriptionId;
        Outcome = plan.BlockedReason is null ? SubscriberMigrationOutcome.Pending : SubscriberMigrationOutcome.Blocked;
        Detail = plan.BlockedReason;
    }

    public Guid MigrationId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public SubscriberMigrationOutcome Outcome { get; private set; }
    public string? Detail { get; private set; }
    public DateTime? EffectiveAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }

    internal void Set(SubscriberMigrationOutcome outcome, string? detail, DateTime? effectiveAtUtc, DateTime nowUtc)
    {
        Outcome = outcome;
        Detail = detail;
        EffectiveAtUtc = effectiveAtUtc ?? EffectiveAtUtc;
        ProcessedAtUtc = nowUtc;
    }
}

/// <summary>
/// D11: la acción explícita de mover las oficinas de un plan a otra edición. La arma el admin de plataforma desde una
/// vista previa confirmada; el job la ejecuta oficina por oficina. Las bloqueadas (no entran en la edición nueva)
/// nunca se tocan. Cancelarla deshace lo agendado que todavía no se aplicó.
/// </summary>
public sealed class SubscriberMigration : TenantlessAggregateRoot
{
    public const int MinNoticeDays = 30;
    public const int MaxNoticeDays = 365;
    public const int MaxReasonLength = 500;

    private readonly List<SubscriberMigrationOffice> _offices = [];

    private SubscriberMigration() { }

    public Guid PlanId { get; private set; }
    public string PlanCode { get; private set; } = default!;
    public Guid ToVersionId { get; private set; }
    public int ToVersionNumber { get; private set; }
    public SubscriberMigrationMode Mode { get; private set; }
    public int NoticeDays { get; private set; }
    public string Reason { get; private set; } = default!;
    public SubscriberMigrationStatus Status { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid RequestedBy { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<SubscriberMigrationOffice> Offices => _offices;

    public static Result<SubscriberMigration> Start(
        Guid planId,
        string planCode,
        Guid toVersionId,
        int toVersionNumber,
        SubscriberMigrationMode mode,
        int noticeDays,
        bool onlyImprovement,
        string reason,
        Guid requestedBy,
        IReadOnlyCollection<SubscriberMigrationOfficePlan> offices,
        DateTime nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Fail("Catalog.ReasonRequired", "A reason is required to move offices to another edition.");
        if (reason.Trim().Length > MaxReasonLength)
            return Fail("Catalog.ReasonTooLong", $"The reason must be {MaxReasonLength} characters or fewer.");

        if (mode == SubscriberMigrationMode.Immediate && !onlyImprovement)
            return Fail(
                "Migration.ImmediateRequiresImprovement",
                "Moving offices right away is only allowed when nobody gets a higher price or fewer limits or modules."
            );

        if (mode == SubscriberMigrationMode.AtNextRenewal && noticeDays is < MinNoticeDays or > MaxNoticeDays)
            return Fail(
                "Migration.NoticeTooShort",
                $"Offices need between {MinNoticeDays} and {MaxNoticeDays} days of notice before the change."
            );

        if (offices.Count == 0)
            return Fail("Migration.NoOffices", "No office is on another edition of this plan.");

        var migration = new SubscriberMigration
        {
            PlanId = planId,
            PlanCode = planCode,
            ToVersionId = toVersionId,
            ToVersionNumber = toVersionNumber,
            Mode = mode,
            NoticeDays = mode == SubscriberMigrationMode.Immediate ? 0 : noticeDays,
            Reason = reason.Trim(),
            Status = SubscriberMigrationStatus.Running,
            RequestedBy = requestedBy,
            CreatedAtUtc = nowUtc,
        };
        foreach (var office in offices)
            migration._offices.Add(new SubscriberMigrationOffice(migration.Id, office));
        migration.CompleteIfDone(nowUtc);
        return Result.Success(migration);
    }

    public IReadOnlyList<Guid> PendingTenantIds() =>
        Status == SubscriberMigrationStatus.Running
            ? _offices.Where(o => o.Outcome == SubscriberMigrationOutcome.Pending).Select(o => o.TenantId).ToList()
            : [];

    /// <summary>Oficinas con un cambio agendado que, al cancelar, hay que deshacer.</summary>
    public IReadOnlyList<Guid> ScheduledTenantIds() =>
        Status == SubscriberMigrationStatus.Cancelled
            ? _offices.Where(o => o.Outcome == SubscriberMigrationOutcome.Scheduled).Select(o => o.TenantId).ToList()
            : [];

    public void MarkApplied(Guid tenantId, DateTime nowUtc) =>
        Mark(tenantId, SubscriberMigrationOutcome.Applied, null, null, nowUtc);

    public void MarkScheduled(Guid tenantId, DateTime effectiveAtUtc, DateTime nowUtc) =>
        Mark(tenantId, SubscriberMigrationOutcome.Scheduled, null, effectiveAtUtc, nowUtc);

    public void MarkBlocked(Guid tenantId, string reason, DateTime nowUtc) =>
        Mark(tenantId, SubscriberMigrationOutcome.Blocked, reason, null, nowUtc);

    public void MarkSkipped(Guid tenantId, string reason, DateTime nowUtc) =>
        Mark(tenantId, SubscriberMigrationOutcome.Skipped, reason, null, nowUtc);

    public void MarkUnscheduled(Guid tenantId, DateTime nowUtc)
    {
        var office = _offices.FirstOrDefault(o => o.TenantId == tenantId);
        if (office?.Outcome == SubscriberMigrationOutcome.Scheduled)
            office.Set(SubscriberMigrationOutcome.Unscheduled, "Migration cancelled", null, nowUtc);
    }

    public Result Cancel(Guid actorUserId, string reason, DateTime nowUtc)
    {
        if (Status == SubscriberMigrationStatus.Cancelled)
            return Result.Success();
        if (Mode == SubscriberMigrationMode.Immediate)
            return Result.Failure(
                new Error("Migration.CannotCancel", "An immediate migration cannot be undone; start a new one instead.")
            );
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Catalog.ReasonRequired", "A reason is required to cancel a migration."));

        Status = SubscriberMigrationStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancelReason = reason.Trim();
        foreach (var office in _offices.Where(o => o.Outcome == SubscriberMigrationOutcome.Pending))
            office.Set(SubscriberMigrationOutcome.Skipped, "Migration cancelled", null, nowUtc);
        return Result.Success();
    }

    private void Mark(
        Guid tenantId,
        SubscriberMigrationOutcome outcome,
        string? detail,
        DateTime? effectiveAtUtc,
        DateTime nowUtc
    )
    {
        var office = _offices.FirstOrDefault(o => o.TenantId == tenantId);
        if (office is null || office.Outcome != SubscriberMigrationOutcome.Pending)
            return;
        office.Set(outcome, detail, effectiveAtUtc, nowUtc);
        CompleteIfDone(nowUtc);
    }

    private void CompleteIfDone(DateTime nowUtc)
    {
        if (
            Status == SubscriberMigrationStatus.Running
            && _offices.All(o => o.Outcome != SubscriberMigrationOutcome.Pending)
        )
        {
            Status = SubscriberMigrationStatus.Completed;
            CompletedAtUtc = nowUtc;
        }
    }

    private static Result<SubscriberMigration> Fail(string code, string message) =>
        Result.Failure<SubscriberMigration>(new Error(code, message));
}
