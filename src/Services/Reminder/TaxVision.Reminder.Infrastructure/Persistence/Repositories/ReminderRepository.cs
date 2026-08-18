using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Infrastructure.Persistence.Repositories;

public sealed class ReminderRepository(ReminderDbContext context) : IReminderRepository
{
    private static readonly ReminderStatus[] PendingStatuses =
    [
        ReminderStatus.Scheduled,
        ReminderStatus.Fired,
        ReminderStatus.Snoozed,
    ];

    /// <summary>
    /// Lo que <b>todavía va a sonar</b>. Distinto de <see cref="PendingStatuses"/>, que incluye
    /// <c>Fired</c> — un recordatorio ya disparado sigue pendiente de que el usuario lo cierre, pero
    /// no aparece en la agenda porque su hora ya pasó.
    /// </summary>
    private static readonly ReminderStatus[] PendingFireStatuses = [ReminderStatus.Scheduled, ReminderStatus.Snoozed];

    public void Add(ReminderAggregate reminder) => context.Reminders.Add(reminder);

    public async Task<Result<ReminderAggregate>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var reminder = await context.Reminders.FirstOrDefaultAsync(r => r.Id == id, ct);
        return reminder is null ? Result.Failure<ReminderAggregate>(ReminderErrors.NotFound) : Result.Success(reminder);
    }

    public async Task<Result<ReminderAggregate>> GetOwnedAsync(
        Guid tenantId,
        Guid userId,
        Guid reminderId,
        CancellationToken ct = default
    )
    {
        var reminder = await context
            .Reminders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == reminderId && r.UserId == userId, ct);

        return reminder is null ? Result.Failure<ReminderAggregate>(ReminderErrors.NotFound) : Result.Success(reminder);
    }

    public async Task<PagedResult<ReminderAggregate>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        ReminderStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = context.Reminders.IgnoreQueryFilters().Where(r => r.TenantId == tenantId && r.UserId == userId);
        if (status is { } wanted)
            query = query.Where(r => r.Status == wanted);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<ReminderAggregate>(items, page, size, total);
    }

    public async Task<PagedResult<ReminderAggregate>> ListUpcomingForUserAsync(
        Guid tenantId,
        Guid userId,
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = context
            .Reminders.IgnoreQueryFilters()
            .Where(r =>
                r.TenantId == tenantId
                && r.UserId == userId
                && PendingFireStatuses.Contains(r.Status)
                && r.Schedule.FireAtUtc >= fromUtc
                && r.Schedule.FireAtUtc <= toUtc
            );

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(r => r.Schedule.FireAtUtc).Skip((page - 1) * size).Take(size).ToListAsync(ct);

        return new PagedResult<ReminderAggregate>(items, page, size, total);
    }

    public async Task<Result<ReminderAggregate>> GetForSchedulerAsync(
        Guid tenantId,
        Guid reminderId,
        CancellationToken ct = default
    )
    {
        var reminder = await context
            .Reminders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == reminderId, ct);

        return reminder is null ? Result.Failure<ReminderAggregate>(ReminderErrors.NotFound) : Result.Success(reminder);
    }

    public Task<ReminderAggregate?> FindByRequestKeyAsync(
        Guid tenantId,
        RequestKey requestKey,
        CancellationToken ct = default
    ) =>
        context
            .Reminders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.RequestKey == requestKey, ct);

    public async Task<IReadOnlyList<ReminderAggregate>> ListPendingByTargetAsync(
        Guid tenantId,
        ReminderCategory category,
        Guid targetId,
        CancellationToken ct = default
    ) =>
        await context
            .Reminders.IgnoreQueryFilters()
            .Where(r =>
                r.TenantId == tenantId
                && r.Target.Category == category
                && r.Target.TargetId == targetId
                && PendingStatuses.Contains(r.Status)
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ReminderAggregate>> ListScheduledWithinHorizonAsync(
        DateTime horizonUtc,
        CancellationToken ct = default
    ) =>
        await context
            .Reminders.IgnoreQueryFilters()
            .Where(r => r.Status == ReminderStatus.Scheduled && r.Schedule.FireAtUtc <= horizonUtc)
            .OrderBy(r => r.Schedule.FireAtUtc)
            .ToListAsync(ct);
}
