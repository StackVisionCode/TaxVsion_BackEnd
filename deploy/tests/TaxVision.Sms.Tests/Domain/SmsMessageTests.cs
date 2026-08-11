using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.ValueObjects;

namespace TaxVision.Sms.Tests.Domain;

public sealed class SmsMessageTests
{
    private static readonly DateTime Now = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    private static SmsMessage NewPending(
        string idempotencyKey = "idem-1",
        string correlationId = "corr-1",
        IReadOnlyList<SmsMediaInput>? media = null
    )
    {
        var result = SmsMessage.Create(
            tenantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            to: PhoneE164.Create("+18095551234").Value,
            body: SmsBody.Create("hi").Value,
            idempotencyKey: idempotencyKey,
            correlationId: correlationId,
            batchId: Guid.NewGuid(),
            providerCode: "fake",
            sourceContext: "unit-test",
            media: media ?? [],
            nowUtc: Now
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void Create_snapshots_fields_and_starts_pending()
    {
        var msg = NewPending(media: [new SmsMediaInput("https://x/y.pdf", "application/pdf", "y.pdf", 100)]);

        Assert.Equal(SmsMessageStatus.Pending, msg.Status);
        Assert.Equal("+18095551234", msg.To);
        Assert.Equal("hi", msg.Body);
        Assert.Equal("unit-test", msg.SourceContext);
        Assert.Single(msg.Media);
        Assert.Equal(Now, msg.CreatedAtUtc);
    }

    [Fact]
    public void Create_generates_correlation_when_blank()
    {
        var msg = NewPending(correlationId: "   ");

        Assert.False(string.IsNullOrWhiteSpace(msg.CorrelationId));
    }

    [Fact]
    public void Create_rejects_empty_tenant()
    {
        var result = SmsMessage.Create(
            Guid.Empty, Guid.NewGuid(), PhoneE164.Create("+18095551234").Value, SmsBody.Create("hi").Value,
            "idem", "corr", Guid.NewGuid(), "fake", null, [], Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal(SmsErrors.InvalidTenant.Code, result.Error.Code);
    }

    [Fact]
    public void Create_rejects_empty_customer()
    {
        var result = SmsMessage.Create(
            Guid.NewGuid(), Guid.Empty, PhoneE164.Create("+18095551234").Value, SmsBody.Create("hi").Value,
            "idem", "corr", Guid.NewGuid(), "fake", null, [], Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal(SmsErrors.InvalidCustomer.Code, result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_idempotency_key(string? key)
    {
        var result = SmsMessage.Create(
            Guid.NewGuid(), Guid.NewGuid(), PhoneE164.Create("+18095551234").Value, SmsBody.Create("hi").Value,
            key!, "corr", Guid.NewGuid(), "fake", null, [], Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal(SmsErrors.InvalidIdempotencyKey.Code, result.Error.Code);
    }

    [Fact]
    public void MarkAccepted_moves_pending_to_accepted_and_records_provider_id()
    {
        var msg = NewPending();

        var result = msg.MarkAccepted("prov-1", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(SmsMessageStatus.Accepted, msg.Status);
        Assert.Equal("prov-1", msg.ProviderMessageId);
        Assert.Equal(Now, msg.AcceptedAtUtc);
    }

    [Fact]
    public void MarkAccepted_is_idempotent()
    {
        var msg = NewPending();
        msg.MarkAccepted("prov-1", Now);

        var again = msg.MarkAccepted("prov-2", Now);

        Assert.True(again.IsSuccess);
        Assert.Equal("prov-1", msg.ProviderMessageId); // unchanged
    }

    [Fact]
    public void MarkDelivered_from_accepted_succeeds_and_is_idempotent()
    {
        var msg = NewPending();
        msg.MarkAccepted("prov-1", Now);

        Assert.True(msg.MarkDelivered(Now).IsSuccess);
        Assert.Equal(SmsMessageStatus.Delivered, msg.Status);
        Assert.Equal(Now, msg.DeliveredAtUtc);

        Assert.True(msg.MarkDelivered(Now).IsSuccess); // replay = no-op
        Assert.Equal(SmsMessageStatus.Delivered, msg.Status);
    }

    [Fact]
    public void MarkDelivered_after_failed_is_invalid_transition()
    {
        var msg = NewPending();
        msg.MarkFailed(Now, "providerRejected", "nope");

        var result = msg.MarkDelivered(Now);

        Assert.True(result.IsFailure);
        Assert.Equal(SmsErrors.InvalidTransition.Code, result.Error.Code);
        Assert.Equal(SmsMessageStatus.Failed, msg.Status);
    }

    [Fact]
    public void MarkFailed_records_code_and_reason()
    {
        var msg = NewPending();

        var result = msg.MarkFailed(Now, "providerRejected", "carrier down");

        Assert.True(result.IsSuccess);
        Assert.Equal(SmsMessageStatus.Failed, msg.Status);
        Assert.Equal("providerRejected", msg.FailureCode);
        Assert.Equal("carrier down", msg.FailureReason);
        Assert.Equal(Now, msg.FailedAtUtc);
    }

    [Fact]
    public void MarkSuppressed_only_from_pending()
    {
        var msg = NewPending();

        Assert.True(msg.MarkSuppressed("opted out", Now).IsSuccess);
        Assert.Equal(SmsMessageStatus.Suppressed, msg.Status);
        Assert.Equal("suppressed", msg.FailureCode);

        // A suppressed message cannot then be accepted.
        var accepted = msg.MarkAccepted("prov-1", Now);
        Assert.True(accepted.IsFailure);
        Assert.Equal(SmsErrors.InvalidTransition.Code, accepted.Error.Code);
    }

    [Fact]
    public void MarkUndeliverable_from_accepted_succeeds()
    {
        var msg = NewPending();
        msg.MarkAccepted("prov-1", Now);

        var result = msg.MarkUndeliverable(Now, "invalidDestination", "unreachable");

        Assert.True(result.IsSuccess);
        Assert.Equal(SmsMessageStatus.Undeliverable, msg.Status);
        Assert.Equal("invalidDestination", msg.FailureCode);
    }
}
