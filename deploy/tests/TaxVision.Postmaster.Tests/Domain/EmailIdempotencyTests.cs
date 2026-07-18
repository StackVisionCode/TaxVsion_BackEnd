using TaxVision.Postmaster.Domain.Idempotency;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class EmailIdempotencyTests
{
    [Fact]
    public void Reserve_creates_pending_reservation()
    {
        var now = DateTime.UtcNow;
        var result = EmailIdempotency.Reserve(Guid.NewGuid(), "key-1", now, TimeSpan.FromDays(7));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CompletedAtUtc);
        Assert.Null(result.Value.SentMessageId);
        Assert.Equal(now.AddDays(7), result.Value.ExpiresAtUtc);
    }

    [Fact]
    public void Reserve_rejects_empty_idempotency_key()
    {
        var result = EmailIdempotency.Reserve(Guid.NewGuid(), "", DateTime.UtcNow, TimeSpan.FromDays(7));

        Assert.True(result.IsFailure);
        Assert.Equal("EmailIdempotency.IdempotencyKey", result.Error.Code);
    }

    [Fact]
    public void Complete_sets_sent_message_id_and_timestamp()
    {
        var reservation = EmailIdempotency
            .Reserve(Guid.NewGuid(), "key-1", DateTime.UtcNow, TimeSpan.FromDays(7))
            .Value;
        var sentMessageId = Guid.NewGuid();

        var result = reservation.Complete(sentMessageId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(sentMessageId, reservation.SentMessageId);
        Assert.NotNull(reservation.CompletedAtUtc);
    }

    [Fact]
    public void Complete_rejects_double_completion()
    {
        var reservation = EmailIdempotency
            .Reserve(Guid.NewGuid(), "key-1", DateTime.UtcNow, TimeSpan.FromDays(7))
            .Value;
        reservation.Complete(Guid.NewGuid(), DateTime.UtcNow);

        var result = reservation.Complete(Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailIdempotency.AlreadyCompleted", result.Error.Code);
    }
}
