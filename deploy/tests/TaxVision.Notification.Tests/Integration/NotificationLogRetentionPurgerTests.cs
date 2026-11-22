using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Retention;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>La purga borra por lotes en SQL, sin traer los logs ni sus intentos a memoria.</summary>
public sealed class NotificationLogRetentionPurgerTests : IAsyncLifetime
{
    private readonly NotificationTempDatabase _database = new("notret_");

    public Task InitializeAsync() => _database.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Deletes_in_batches_without_loading_entities()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var oldA = CreateLog(tenantA, "old-a@example.com", DateTime.UtcNow.AddMonths(-13));
        var oldB = CreateLog(tenantB, "old-b@example.com", DateTime.UtcNow.AddMonths(-14));
        var recent = CreateLog(tenantA, "recent@example.com", DateTime.UtcNow.AddMonths(-1));
        oldA.AddDispatchAttempt(NotificationChannel.Email);
        oldB.AddDispatchAttempt(NotificationChannel.Email);
        await using (var seed = _database.NewContext())
        {
            seed.NotificationLogs.AddRange(oldA, oldB, recent);
            await seed.SaveChangesAsync();
        }

        var commands = new CommandRecorder();
        int purged;
        await using (var db = ContextWith(commands))
            purged = await new NotificationLogRetentionPurger(db).PurgeAsync(
                DateTime.UtcNow.AddMonths(-12),
                batchSize: 1,
                CancellationToken.None
            );

        Assert.Equal(2, purged);
        Assert.DoesNotContain(commands.Texts, text => text.TrimStart().StartsWith("SELECT", StringComparison.Ordinal));
        await using var verify = _database.NewContext();
        Assert.Equal(recent.Id, Assert.Single(await verify.NotificationLogs.IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Empty(await verify.NotificationDispatchAttempts.IgnoreQueryFilters().ToListAsync());
    }

    private NotificationDbContext ContextWith(CommandRecorder commands) =>
        new(
            new DbContextOptionsBuilder<NotificationDbContext>()
                .UseSqlServer(_database.ConnectionString)
                .AddInterceptors(commands)
                .Options,
            new NotificationTempDatabase.NoTenant()
        );

    private static NotificationLog CreateLog(Guid tenantId, string recipient, DateTime createdAtUtc)
    {
        var log = NotificationLog
            .Create(
                tenantId,
                NotificationChannel.Email,
                NotificationRecipient.ForEmail(recipient).Value,
                "Subject",
                "template",
                null,
                null
            )
            .Value;
        typeof(NotificationLog).GetProperty(nameof(NotificationLog.CreatedAtUtc))!.SetValue(log, createdAtUtc);
        return log;
    }

    private sealed class CommandRecorder : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Texts.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Texts.Add(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
