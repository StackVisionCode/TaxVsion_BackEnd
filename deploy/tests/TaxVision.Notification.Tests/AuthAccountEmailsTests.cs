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
/// Una persona puede tener cuenta de staff y de portal en la misma oficina con el mismo email: cada correo de
/// cuenta dice de cuál habla y enlaza a su superficie.
/// </summary>
public sealed class AuthAccountEmailsTests
{
    private static readonly IOptions<PortalOptions> Portal = Options.Create(
        new PortalOptions
        {
            BaseUrl = "https://app.test",
            ClientBaseUrl = "https://app.test",
            ProductName = "TaxProffice",
        }
    );

    private const string Host = "acme.taxproffice.com";

    [Fact]
    public async Task A_portal_reset_links_to_the_portal_and_names_the_client_account_and_office()
    {
        var scribe = new CapturingScribe();

        await PasswordResetRequestedConsumer.Handle(
            new PasswordResetRequestedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Email = "ana@example.com",
                RawToken = "tok",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
                ActorType = "CustomerPortal",
                TenantName = "Acme Tax",
            },
            new CapturingGateway(),
            scribe,
            Portal,
            new FakeTenantHostResolver(Host),
            new Correlation(),
            CancellationToken.None
        );

        var variables = scribe.Rendered.Single().Variables;
        Assert.Equal($"https://{Host}/portal/client/auth/reset-password/new?token=tok", variables["reset_link"]);
        Assert.Equal("portal", variables["account_kind"]);
        Assert.Equal("Acme Tax", variables["office"]);
    }

    [Fact]
    public async Task A_workspace_reset_keeps_linking_to_the_office_workspace()
    {
        var scribe = new CapturingScribe();

        await PasswordResetRequestedConsumer.Handle(
            new PasswordResetRequestedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Email = "ana@example.com",
                RawToken = "tok",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
                ActorType = "TenantEmployee",
                TenantName = "Acme Tax",
            },
            new CapturingGateway(),
            scribe,
            Portal,
            new FakeTenantHostResolver(Host),
            new Correlation(),
            CancellationToken.None
        );

        var variables = scribe.Rendered.Single().Variables;
        Assert.Equal($"https://{Host}/reset-password?token=tok", variables["reset_link"]);
        Assert.Equal("workspace", variables["account_kind"]);
    }

    [Fact]
    public async Task A_client_confirms_a_new_email_in_the_portal_and_the_old_address_is_told_which_account()
    {
        var scribe = new CapturingScribe();
        var gateway = new CapturingGateway();

        await EmailChangeRequestedConsumer.Handle(
            new EmailChangeRequestedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                CurrentEmail = "ana@example.com",
                NewEmail = "ana.new@example.com",
                RawToken = "tok",
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                ActorType = "CustomerPortal",
                TenantName = "Acme Tax",
            },
            gateway,
            scribe,
            Portal,
            new FakeTenantHostResolver(Host),
            new Correlation(),
            CancellationToken.None
        );

        var confirm = scribe.Rendered.First(render => render.EventKey == "auth.email_change_requested.v1").Variables;
        Assert.Equal($"https://{Host}/portal/client/auth/confirm-email?token=tok", confirm["confirm_link"]);
        Assert.Equal("portal", confirm["account_kind"]);
        var warning = scribe
            .Rendered.First(render => render.EventKey == "auth.email_change_security_alert.v1")
            .Variables;
        Assert.Contains("client portal account at Acme Tax", (string)warning["description"]!);
        Assert.Contains(gateway.Requests, request => request.To == "ana@example.com");
    }

    [Fact]
    public async Task A_portal_invitation_is_worded_for_the_client_and_never_uses_a_spanish_default()
    {
        var scribe = new CapturingScribe();

        await InvitationCreatedConsumer.Handle(
            new InvitationCreatedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                InvitationId = Guid.NewGuid(),
                Email = "ana@example.com",
                ActorType = "CustomerPortal",
                RawToken = "tok",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                TenantName = "Acme Tax",
                TenantSubdomain = "acme",
            },
            new CapturingGateway(),
            scribe,
            Portal,
            new Correlation(),
            CancellationToken.None
        );

        var variables = scribe.Rendered.Single().Variables;
        Assert.Equal("portal", variables["account_kind"]);
        Assert.Equal("Acme Tax", variables["inviter"]);
    }

    private sealed record Render(string EventKey, IReadOnlyDictionary<string, object?> Variables);

    private sealed class CapturingScribe : IScribeRenderClient
    {
        public List<Render> Rendered { get; } = [];

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        )
        {
            Rendered.Add(new Render(eventKey, variables));
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("subject", "<p>body</p>", "body")));
        }
    }

    private sealed class CapturingGateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Requests { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationDispatchAttemptStatus.Sent,
                    null,
                    null
                )
            );
        }
    }

    private sealed class Correlation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
