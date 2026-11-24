using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.ClientForms.Integration;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>Tiempo a mano contra SQL real: se guarda, se corrige, se borra la fila y el índice frena dos relojes.</summary>
public sealed class TaskTimeEntriesIntegrationTests : IAsyncLifetime
{
    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _ana = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task A_manual_entry_is_saved_corrected_and_removed_with_the_total_in_step()
    {
        var task = NewTask();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = task.LogTime(
            _ana,
            new WorkDay(today, today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), today),
            45,
            true,
            "W-2",
            DateTime.UtcNow
        ).Value;
        await using (var db = _database.NewContext(_tenantId))
        {
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext(_tenantId))
        {
            var loaded = await db.Tasks.Include(t => t.Timers).SingleAsync(t => t.Id == task.Id);
            Assert.True(loaded.EditTimeEntry(entry.Id, _ana, false, 90, false, "Corrected", DateTime.UtcNow).IsSuccess);
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext(_tenantId))
        {
            var loaded = await db.Tasks.Include(t => t.Timers).SingleAsync(t => t.Id == task.Id);
            var saved = Assert.Single(loaded.Timers);
            Assert.Equal(
                (true, false, "Corrected", 1.5m),
                (saved.IsManual, saved.IsBillable, saved.Note, loaded.ActualHours)
            );
            Assert.True(loaded.RemoveTimeEntry(entry.Id, _ana, false, DateTime.UtcNow).IsSuccess);
            await db.SaveChangesAsync();
        }

        await using var check = _database.NewContext(_tenantId);
        Assert.Equal(0, await check.Set<TaskTimer>().CountAsync(t => t.TaskId == task.Id));
        Assert.Equal(0m, (await check.Tasks.SingleAsync(t => t.Id == task.Id)).ActualHours);
    }

    [Fact]
    public async Task The_database_refuses_a_second_running_timer_for_the_same_person_and_task()
    {
        var task = NewTask();
        task.StartTimer(_ana, true, DateTime.UtcNow);
        await using (var db = _database.NewContext(_tenantId))
        {
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
        }

        await using var other = _database.NewContext(_tenantId);
        var timerId = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            other.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO TaskTimers (Id, TaskId, UserId, StartedAtUtc, IsBillable, IsManual) VALUES ({timerId}, {task.Id}, {_ana}, {DateTime.UtcNow}, 1, 0)"
            )
        );
    }

    private TaskItem NewTask() =>
        TaskItem
            .Create(
                _tenantId,
                _ana,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                _ana,
                DateTime.UtcNow
            )
            .Value;
}
