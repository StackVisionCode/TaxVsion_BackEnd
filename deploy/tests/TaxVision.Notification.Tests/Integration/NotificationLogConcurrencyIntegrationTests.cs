using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Domain.Emailing.Sending;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>NOT-08: dos callbacks del mismo correo ya no se pisan.</summary>
public sealed class NotificationLogConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly NotificationTempDatabase _database = new("notn08_");
    private readonly Guid _tenant = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private async Task<(Guid LogId, Guid AttemptId)> SeedSentAsync()
    {
        var log = NotificationLog
            .Create(_tenant, NotificationChannel.Email, "to@not08.test", "Subject", "tpl", null, null)
            .Value;
        var attempt = log.AddDispatchAttempt(NotificationChannel.Email);
        log.UpdateAttemptStatus(attempt.Id, NotificationDispatchAttemptStatus.Sent, "pm-1");
        log.MarkSent();
        await using var seed = _database.NewContext();
        seed.NotificationLogs.Add(log);
        await seed.SaveChangesAsync();
        return (log.Id, attempt.Id);
    }

    private static Task<NotificationLog> LoadAsync(
        TaxVision.Notification.Infrastructure.Persistence.NotificationDbContext db,
        Guid id
    ) => db.NotificationLogs.IgnoreQueryFilters().Include(l => l.Attempts).FirstAsync(l => l.Id == id);

    [Fact]
    public async Task Delivered_and_bounced_in_parallel_keep_the_bounce()
    {
        var (logId, attemptId) = await SeedSentAsync();
        await using var bounceDb = _database.NewContext();
        await using var deliveredDb = _database.NewContext();
        var bouncing = await LoadAsync(bounceDb, logId);
        var delivering = await LoadAsync(deliveredDb, logId);

        Assert.True(
            bouncing
                .UpdateAttemptStatus(attemptId, NotificationDispatchAttemptStatus.Bounced, errorReason: "Hard: gone")
                .IsSuccess
        );
        bouncing.MarkBounced("Bounce (Hard): gone");
        await bounceDb.SaveChangesAsync();

        delivering.UpdateAttemptStatus(attemptId, NotificationDispatchAttemptStatus.Delivered);
        delivering.MarkDelivered();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => deliveredDb.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);

        // El reintento recarga: la máquina de estados no deja volver a Delivered.
        await using var retryDb = _database.NewContext();
        var retried = await LoadAsync(retryDb, logId);
        var retry = retried.UpdateAttemptStatus(attemptId, NotificationDispatchAttemptStatus.Delivered);
        Assert.True(retry.IsFailure);

        await using var check = _database.NewContext();
        var final = await LoadAsync(check, logId);
        Assert.Equal(NotificationStatus.Bounced, final.Status);
        Assert.Equal(NotificationDispatchAttemptStatus.Bounced, final.Attempts.Single().Status);
    }

    [Fact]
    public async Task Attempt_change_without_a_status_change_on_the_log_still_conflicts()
    {
        var (logId, attemptId) = await SeedSentAsync();
        await using var firstDb = _database.NewContext();
        await using var secondDb = _database.NewContext();
        var first = await LoadAsync(firstDb, logId);
        var second = await LoadAsync(secondDb, logId);

        first.UpdateAttemptStatus(attemptId, NotificationDispatchAttemptStatus.Delivered);
        await firstDb.SaveChangesAsync();

        // El log sigue en Sent: sin tocar la raíz, el token no vería el cambio del intento.
        second.UpdateAttemptStatus(attemptId, NotificationDispatchAttemptStatus.Bounced, errorReason: "Hard: gone");
        await Assert.ThrowsAsync<ConflictException>(() => secondDb.SaveChangesAsync());
    }

    [Fact]
    public async Task OutboundEmailMessage_second_concurrent_save_conflicts()
    {
        var message = OutboundEmailMessage
            .Create(
                _tenant,
                "Subject",
                "<p>Body</p>",
                "Body",
                EmailPriority.Normal,
                [("to@not08.test", EmailRecipientKind.To, null)],
                "[]",
                null,
                null,
                null,
                null
            )
            .Value;
        await using (var seed = _database.NewContext())
        {
            seed.OutboundEmailMessages.Add(message);
            await seed.SaveChangesAsync();
        }

        await using var firstDb = _database.NewContext();
        await using var secondDb = _database.NewContext();
        var first = await firstDb.OutboundEmailMessages.IgnoreQueryFilters().FirstAsync(m => m.Id == message.Id);
        var second = await secondDb.OutboundEmailMessages.IgnoreQueryFilters().FirstAsync(m => m.Id == message.Id);

        first.MarkBounced("gone");
        await firstDb.SaveChangesAsync();

        second.MarkDelivered();
        await Assert.ThrowsAsync<ConflictException>(() => secondDb.SaveChangesAsync());
    }
}
