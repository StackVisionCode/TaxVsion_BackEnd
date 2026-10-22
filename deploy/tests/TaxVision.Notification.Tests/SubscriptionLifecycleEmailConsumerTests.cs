using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El aviso del ciclo de vida de la suscripción elige plantilla por estado y, para la cancelación programada,
/// por motivo: cancelar al fin del período deja la suscripción Active, así que el estado solo no alcanza.
/// </summary>
public sealed class SubscriptionLifecycleEmailConsumerTests
{
    private static readonly PortalOptions Portal = new();

    [Fact]
    public async Task A_scheduled_cancellation_sends_its_own_template_with_the_end_date()
    {
        var gateway = new RecordingEmailDispatchGateway();
        var scribe = new CapturingScribeRenderClient();
        var evt = Event(
            "Active",
            "CancellationScheduled",
            accessEndsAtUtc: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        );

        await HandleAsync(evt, gateway, scribe);

        var queued = Assert.Single(gateway.Queued);
        Assert.Equal("subscription.cancellation_scheduled", queued.TemplateKey);
        Assert.Equal("owner@acme.test", queued.To);
        Assert.Equal(evt.EventId, queued.RelatedEventId);
        Assert.Equal("subscription.cancellation_scheduled.v1", scribe.LastEventKey);
        Assert.Equal("October 1, 2026", scribe.LastVariables!["access_ends_date"]);
    }

    // El recordatorio lo publica un job: cada pasada trae un EventId nuevo, así que la deduplicación por
    // evento no protege. La clave estable es lo único que evita mandarlo dos veces el mismo día.
    [Fact]
    public async Task The_access_ending_reminder_carries_a_stable_idempotency_key()
    {
        var gateway = new RecordingEmailDispatchGateway();
        var scribe = new CapturingScribeRenderClient();
        var accessEndsAtUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var evt = Event("Active", "AccessEnding", accessEndsAtUtc);

        await HandleAsync(evt, gateway, scribe);
        await HandleAsync(evt with { }, gateway, scribe);

        Assert.Equal(2, gateway.Queued.Count);
        Assert.Equal("subscription.access_ending", gateway.Queued[0].TemplateKey);
        Assert.Equal($"{evt.TenantId:N}:access-ending:20261001", gateway.Queued[0].IdempotencyKey);
        // Dos pasadas del job, la MISMA clave: el guard de Postmaster manda uno solo.
        Assert.Equal(gateway.Queued[0].IdempotencyKey, gateway.Queued[1].IdempotencyKey);
    }

    // Los otros avisos se deduplican por evento, así que no llevan clave propia.
    [Fact]
    public async Task Other_subscription_emails_keep_deduplicating_by_event()
    {
        var gateway = new RecordingEmailDispatchGateway();

        await HandleAsync(Event("Expired", "CancellationEnded"), gateway, new CapturingScribeRenderClient());

        Assert.Null(Assert.Single(gateway.Queued).IdempotencyKey);
    }

    // Sin ese motivo, Active sigue significando "reactivada": la cancelación no puede robarse esa plantilla.
    [Fact]
    public async Task An_active_subscription_recovered_still_sends_the_reactivated_template()
    {
        var gateway = new RecordingEmailDispatchGateway();
        var scribe = new CapturingScribeRenderClient();

        await HandleAsync(Event("Active", "PaymentRecovered"), gateway, scribe);

        Assert.Equal("subscription.reactivated", Assert.Single(gateway.Queued).TemplateKey);
    }

    [Fact]
    public async Task An_unknown_status_sends_nothing()
    {
        var gateway = new RecordingEmailDispatchGateway();

        await HandleAsync(Event("Draft", "Unknown"), gateway, new CapturingScribeRenderClient());

        Assert.Empty(gateway.Queued);
    }

    // ---------- Cobro automático de renovación fallido: un correo por intento ----------

    [Fact]
    public async Task A_failed_charge_attempt_sends_the_failed_charge_template_with_the_retry_date()
    {
        var gateway = new RecordingEmailDispatchGateway();
        var scribe = new CapturingScribeRenderClient();
        var evt = ChargeFailed(attempt: 2, isFinal: false);

        await HandleAsync(evt, gateway, scribe);

        var queued = Assert.Single(gateway.Queued);
        Assert.Equal("subscription.recurring_charge_failed", queued.TemplateKey);
        Assert.Equal("subscription.recurring_charge_failed.v1", scribe.LastEventKey);
        var vars = scribe.LastVariables!;
        Assert.Equal("Professional plan", vars["item_description"]);
        Assert.Equal("$49.00", vars["amount"]);
        Assert.Equal("Visa", vars["card_brand"]);
        Assert.Equal(true, vars["has_card"]);
        Assert.Equal("2", vars["attempt_number"]);
        Assert.Equal("4", vars["max_attempts"]);
        Assert.Equal(false, vars["is_final"]);
        Assert.Equal("October 23, 2026", vars["next_retry_date"]);
        // Nunca el código crudo del proveedor.
        Assert.Equal("Your bank declined the charge.", vars["friendly_reason"]);
        Assert.Equal("https://www.taxoffix.io/account/plan", vars["manage_url"]);
    }

    // El último intento llega con la suscripción ya suspendida: el correo de cobro fallido lo explica, no
    // el de "suspended".
    [Fact]
    public async Task The_final_attempt_uses_the_failed_charge_template_even_when_suspended()
    {
        var gateway = new RecordingEmailDispatchGateway();
        var scribe = new CapturingScribeRenderClient();

        await HandleAsync(ChargeFailed(attempt: 4, isFinal: true), gateway, scribe);

        Assert.Equal("subscription.recurring_charge_failed", Assert.Single(gateway.Queued).TemplateKey);
        Assert.Equal(true, scribe.LastVariables!["is_final"]);
        Assert.Equal(
            "your subscription has been suspended and your team can no longer sign in",
            scribe.LastVariables["suspension_effect"]
        );
    }

    // Un reenvío del mismo aviso (otro EventId) no repite el correo; el intento siguiente sí sale.
    [Fact]
    public async Task Each_attempt_has_its_own_idempotency_key()
    {
        var gateway = new RecordingEmailDispatchGateway();
        var paymentId = Guid.NewGuid();

        await HandleAsync(ChargeFailed(2, false, paymentId), gateway, new CapturingScribeRenderClient());
        await HandleAsync(ChargeFailed(2, false, paymentId), gateway, new CapturingScribeRenderClient());
        await HandleAsync(ChargeFailed(3, false, paymentId), gateway, new CapturingScribeRenderClient());

        Assert.Equal($"{paymentId:N}:attempt-2:owner@acme.test", gateway.Queued[0].IdempotencyKey);
        Assert.Equal(gateway.Queued[0].IdempotencyKey, gateway.Queued[1].IdempotencyKey);
        Assert.Equal($"{paymentId:N}:attempt-3:owner@acme.test", gateway.Queued[2].IdempotencyKey);
    }

    private static TenantSubscriptionEmailRequestedIntegrationEvent ChargeFailed(
        int attempt,
        bool isFinal,
        Guid? paymentId = null
    ) =>
        Event(isFinal ? "Suspended" : "Active", "RenewalChargeFailed") with
        {
            Email = "Owner@Acme.test",
            SaaSPaymentId = paymentId ?? Guid.NewGuid(),
            AttemptNumber = attempt,
            MaxAttempts = 4,
            IsFinalAttempt = isFinal,
            NextAttemptAtUtc = isFinal ? null : new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc),
            ItemDescription = "Professional plan",
            Amount = "$49.00",
            CardBrand = "visa",
            CardLast4 = "4242",
            FailureCode = "card_declined",
            Consequence = isFinal ? "SubscriptionSuspended" : null,
            ManageUrl = "https://www.taxoffix.io/account/plan",
        };

    private static TenantSubscriptionEmailRequestedIntegrationEvent Event(
        string status,
        string reason,
        DateTime? accessEndsAtUtc = null
    ) =>
        new()
        {
            TenantId = Guid.NewGuid(),
            Email = "owner@acme.test",
            FirstName = "Ada",
            Status = status,
            Reason = reason,
            PlanName = "Pro",
            AccessEndsAtUtc = accessEndsAtUtc,
            RenewUrl = "https://acme.taxproffice.com",
        };

    private static Task HandleAsync(
        TenantSubscriptionEmailRequestedIntegrationEvent evt,
        RecordingEmailDispatchGateway gateway,
        CapturingScribeRenderClient scribe
    ) =>
        SubscriptionLifecycleEmailConsumer.Handle(
            evt,
            gateway,
            scribe,
            Options.Create(Portal),
            new NoOpCorrelationContext(),
            new NoOpSubscriptionEmailMetrics(),
            CancellationToken.None
        );

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new NoOpDisposable();
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class NoOpSubscriptionEmailMetrics : ISubscriptionEmailMetrics
    {
        public void RecordDunningEmailSent(string templateKey) { }
    }

    private sealed class CapturingScribeRenderClient : IScribeRenderClient
    {
        public string? LastEventKey { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastVariables { get; private set; }

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        )
        {
            LastEventKey = eventKey;
            LastVariables = variables;
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("Subject", "<p>Body</p>", null)));
        }
    }

    private sealed class RecordingEmailDispatchGateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Queued { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Queued.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationDispatchAttemptStatus.Queued,
                    null,
                    null
                )
            );
        }
    }
}
