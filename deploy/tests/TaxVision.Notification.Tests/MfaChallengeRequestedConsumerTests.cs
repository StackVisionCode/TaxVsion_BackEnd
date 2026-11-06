using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Recipients;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El código por correo no trae el tipo de actor: el usuario es cliente si es el de una cuenta de portal
/// proyectada, y entonces el correo lleva el logo del portal.
/// </summary>
public sealed class MfaChallengeRequestedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task The_code_for_a_portal_user_is_rendered_for_the_client()
    {
        var userId = Guid.NewGuid();
        var scribe = new AudienceScribe();

        await HandleAsync(userId, scribe, new PortalAccounts(userId));

        Assert.Equal(ScribeRenderAudience.Client, scribe.LastAudience);
    }

    [Fact]
    public async Task The_code_for_a_staff_user_is_rendered_for_the_team()
    {
        var scribe = new AudienceScribe();

        await HandleAsync(Guid.NewGuid(), scribe, new PortalAccounts(portalUserId: null));

        Assert.Equal(ScribeRenderAudience.Staff, scribe.LastAudience);
    }

    private static Task HandleAsync(Guid userId, AudienceScribe scribe, PortalAccounts portalAccounts) =>
        MfaChallengeRequestedConsumer.Handle(
            new MfaChallengeRequestedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Channel = "Email",
                Destination = "ana@example.com",
                Code = "123456",
                Purpose = "login",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            },
            new NoopGateway(),
            scribe,
            portalAccounts,
            new NotificationDispatcherHarness().Dispatcher,
            Options.Create(new PortalOptions()),
            TestPlatform.Settings,
            new NoopCorrelation(),
            CancellationToken.None
        );

    private sealed class AudienceScribe : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => TestDoubles.ScribeTestDefaults.TextUnsupported();

        public ScribeRenderAudience? LastAudience { get; private set; }

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ScribeRenderAudience.Staff, ct);

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        )
        {
            LastAudience = audience;
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("subject", "<p>body</p>", "body")));
        }
    }

    private sealed class PortalAccounts(Guid? portalUserId) : ICustomerPortalAccountProjectionRepository
    {
        public Task<Guid?> GetActivePortalUserIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
            Task.FromResult(portalUserId);

        public Task<CustomerPortalAccountProjection?> GetByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerPortalAccountProjection?>(null);

        public Task<CustomerPortalAccountProjection?> GetByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                portalUserId == userId ? CustomerPortalAccountProjection.Create(tenantId, Guid.NewGuid(), userId) : null
            );

        public Task AddAsync(CustomerPortalAccountProjection projection, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopGateway : IEmailDispatchGateway
    {
        public Task<EmailDispatchResult> QueueEmailAsync(
            EmailDispatchRequest request,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationDispatchAttemptStatus.Sent,
                    null,
                    null
                )
            );
    }

    private sealed class NoopCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
