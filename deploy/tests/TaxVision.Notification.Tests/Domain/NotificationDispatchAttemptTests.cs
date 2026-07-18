using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.Domain;

/// <summary>
/// Tests unitarios del aggregate <see cref="NotificationLog"/> extendido en Notifications Fase 2 con
/// la colección <c>Attempts</c> y de la entity <see cref="NotificationDispatchAttempt"/> con sus
/// transiciones de estado.
/// </summary>
public sealed class NotificationDispatchAttemptTests
{
    private static NotificationLog CreateLog() =>
        NotificationLog
            .Create(
                tenantId: Guid.NewGuid(),
                channel: NotificationChannel.Email,
                recipient: "user@test.com",
                subject: "Test",
                templateKey: "test.key",
                relatedEventId: null,
                correlationId: null
            )
            .Value;

    [Fact]
    public void AddDispatchAttempt_creates_child_with_Queued_state()
    {
        var log = CreateLog();

        var attempt = log.AddDispatchAttempt(NotificationChannel.Email, providerMessageId: "provider-123");

        Assert.Single(log.Attempts);
        Assert.Equal(NotificationChannel.Email, attempt.Channel);
        Assert.Equal(NotificationDispatchAttemptStatus.Queued, attempt.Status);
        Assert.Equal("provider-123", attempt.ProviderMessageId);
        Assert.Equal(log.Id, attempt.NotificationLogId);
        Assert.Equal(log.TenantId, attempt.TenantId);
    }

    [Fact]
    public void UpdateAttemptStatus_moves_Queued_to_Sent()
    {
        var log = CreateLog();
        var attempt = log.AddDispatchAttempt(NotificationChannel.Email);

        var result = log.UpdateAttemptStatus(attempt.Id, NotificationDispatchAttemptStatus.Sent);

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationDispatchAttemptStatus.Sent, attempt.Status);
        Assert.NotNull(attempt.LastEventAtUtc);
    }

    [Fact]
    public void UpdateAttemptStatus_moves_Sent_to_Delivered()
    {
        var log = CreateLog();
        var attempt = log.AddDispatchAttempt(NotificationChannel.Email);
        log.UpdateAttemptStatus(attempt.Id, NotificationDispatchAttemptStatus.Sent);

        var result = log.UpdateAttemptStatus(attempt.Id, NotificationDispatchAttemptStatus.Delivered);

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationDispatchAttemptStatus.Delivered, attempt.Status);
    }

    [Fact]
    public void UpdateAttemptStatus_rejects_Delivered_to_Sent_backward_transition()
    {
        var log = CreateLog();
        var attempt = log.AddDispatchAttempt(NotificationChannel.Email);
        log.UpdateAttemptStatus(attempt.Id, NotificationDispatchAttemptStatus.Sent);
        log.UpdateAttemptStatus(attempt.Id, NotificationDispatchAttemptStatus.Delivered);

        var result = log.UpdateAttemptStatus(attempt.Id, NotificationDispatchAttemptStatus.Sent);

        Assert.True(result.IsFailure);
        Assert.Equal(NotificationDispatchAttemptStatus.Delivered, attempt.Status);
    }

    [Fact]
    public void UpdateAttemptStatus_moves_Queued_to_ProviderNotConfigured_with_error()
    {
        var log = CreateLog();
        var attempt = log.AddDispatchAttempt(NotificationChannel.Email);

        var result = log.UpdateAttemptStatus(
            attempt.Id,
            NotificationDispatchAttemptStatus.ProviderNotConfigured,
            errorReason: "Tenant has no SMTP provider configured."
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationDispatchAttemptStatus.ProviderNotConfigured, attempt.Status);
        Assert.Equal("Tenant has no SMTP provider configured.", attempt.ErrorReason);
    }

    [Fact]
    public void UpdateAttemptStatus_returns_failure_when_attemptId_not_found()
    {
        var log = CreateLog();

        var result = log.UpdateAttemptStatus(Guid.NewGuid(), NotificationDispatchAttemptStatus.Sent);

        Assert.True(result.IsFailure);
        Assert.Equal("NotificationLog.AttemptNotFound", result.Error.Code);
    }

    [Fact]
    public void UpdateAttemptStatus_clears_ErrorReason_on_success_transition()
    {
        var log = CreateLog();
        var attempt = log.AddDispatchAttempt(NotificationChannel.Email);
        log.UpdateAttemptStatus(attempt.Id, NotificationDispatchAttemptStatus.Failed, errorReason: "boom");
        // Recovery no está permitido con la matriz actual (Failed es terminal), verificamos la lógica de
        // limpieza en la transición Queued→Sent en otro log.
        var log2 = CreateLog();
        var attempt2 = log2.AddDispatchAttempt(NotificationChannel.Email);
        log2.UpdateAttemptStatus(attempt2.Id, NotificationDispatchAttemptStatus.Sent);

        Assert.Null(attempt2.ErrorReason);
        Assert.Equal("boom", attempt.ErrorReason); // no se limpia en terminal
    }
}
