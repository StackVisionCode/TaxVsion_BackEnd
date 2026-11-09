using System.Net;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Application.Consumers.Signature;
using TaxVision.Notification.Infrastructure.Scribe;
using TaxVision.Notification.Tests.TestDoubles;
using Wolverine;
using Wolverine.Tracking;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Hardening Fase 7 — prueba que un fallo de <see cref="IScribeRenderClient.RenderAsync"/> ya NO se
/// traduce en un <c>return</c> silencioso que descarta el email. Antes del fix, estos mismos
/// escenarios completaban el handler "con éxito" desde el punto de vista de Wolverine (sin lanzar),
/// así que la política de retry+cooldown (<c>Program.cs</c>, <c>OnException&lt;Exception&gt;</c>)
/// nunca se activaba. Ahora deben propagar <see cref="ScribeRenderFailedException"/>.
/// </summary>
public sealed class ScribeRenderFailureTests
{
    // ------------------------------------------------------------------
    // Unidad: la extensión que centraliza el fix.
    // ------------------------------------------------------------------

    [Fact]
    public void EnsureRendered_returns_value_when_render_succeeded()
    {
        var email = new ScribeRenderedEmail("Subject", "<p>Html</p>", "Text");
        var result = Result.Success(email);

        var rendered = result.EnsureRendered("some.event.v1");

        Assert.Same(email, rendered);
    }

    [Fact]
    public void EnsureRendered_throws_ScribeRenderFailedException_carrying_original_error_when_render_failed()
    {
        var error = new Error("Email.ScribeRender", "Scribe render request failed (503).");
        var result = Result.Failure<ScribeRenderedEmail>(error);

        var thrown = Assert.Throws<ScribeRenderFailedException>(() => result.EnsureRendered("auth.welcome.v1"));

        Assert.Equal("auth.welcome.v1", thrown.EventKey);
        Assert.Equal(error, thrown.ScribeError);
        Assert.Contains("auth.welcome.v1", thrown.Message);
        Assert.Contains(error.Message, thrown.Message);
    }

    // ------------------------------------------------------------------
    // SCR-11: qué se reintenta y qué va directo a la cola de errores.
    // ------------------------------------------------------------------

    [Fact]
    public async Task No_mapping_goes_straight_to_error_queue()
    {
        var thrown = await RenderEmailAgainst(
            HttpStatusCode.NotFound,
            """{"code":"EmailRenderer.NoMapping","message":"No template is mapped."}"""
        );

        Assert.IsAssignableFrom<ArgumentException>(thrown);
        Assert.Contains("EmailRenderer.NoMapping", thrown.Message);
    }

    [Fact]
    public async Task No_mapping_of_a_push_text_goes_straight_to_error_queue()
    {
        var client = ClientReturning(
            HttpStatusCode.NotFound,
            """{"code":"EmailRenderer.NoPublishedVersion","message":"No version."}"""
        );

        var result = await client.RenderTextAsync("some.event.v1", Guid.NewGuid(), new Dictionary<string, object?>());
        var thrown = Record.Exception(() => result.EnsureRendered("some.event.v1"));

        Assert.IsAssignableFrom<ArgumentException>(thrown);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Other_typed_rejections_go_straight_to_error_queue(HttpStatusCode status)
    {
        var thrown = await RenderEmailAgainst(status, """{"code":"EmailRenderer.Parse","message":"Bad template."}""");

        Assert.IsAssignableFrom<ArgumentException>(thrown);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"code":"EmailRenderer.Timeout","message":"Too slow."}""")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    [InlineData(HttpStatusCode.Forbidden, """{"code":"Auth.Forbidden","message":"Stale permissions."}""")]
    [InlineData(HttpStatusCode.RequestTimeout, "")]
    [InlineData(HttpStatusCode.Conflict, """{"code":"Persistence.ConcurrencyConflict","message":"Conflict."}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"code":"RateLimit.Exceeded","message":"Slow down."}""")]
    [InlineData(HttpStatusCode.NotFound, "<html>gateway</html>")]
    [InlineData(HttpStatusCode.NotFound, "")]
    [InlineData(HttpStatusCode.BadRequest, """{"title":"One or more validation errors occurred."}""")]
    public async Task Server_error_is_retried(HttpStatusCode status, string body)
    {
        var thrown = await RenderEmailAgainst(status, body);

        Assert.IsType<ScribeRenderFailedException>(thrown);
    }

    [Fact]
    public async Task Rejected_render_is_not_retried_by_the_standard_policy()
    {
        // Host real con la política del servicio: un intento y a la cola de errores.
        RejectedRenderHandler.Attempts = 0;
        using var host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(RejectedRenderHandler));
                options.UseTaxVisionConflictPolicy();
                options.ApplyStandardFailurePolicies();
            })
            .StartAsync();

        var tracked = await host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .Timeout(TimeSpan.FromSeconds(30))
            .PublishMessageAndWaitAsync(new RejectedRender());

        Assert.Equal(1, RejectedRenderHandler.Attempts);
        Assert.Single(tracked.MovedToErrorQueue.MessagesOf<RejectedRender>());
    }

    public sealed record RejectedRender;

    public static class RejectedRenderHandler
    {
        public static int Attempts;

        public static void Handle(RejectedRender _)
        {
            Interlocked.Increment(ref Attempts);
            Result
                .Failure<ScribeRenderedEmail>(new Error(ScribeRenderRejectedException.ErrorCode, "Rejected."))
                .EnsureRendered("some.event.v1");
        }
    }

    private static async Task<Exception?> RenderEmailAgainst(HttpStatusCode status, string body)
    {
        var result = await ClientReturning(status, body)
            .RenderAsync("some.event.v1", Guid.NewGuid(), new Dictionary<string, object?>());

        return Record.Exception(() => result.EnsureRendered("some.event.v1"));
    }

    private static ScribeRenderClient ClientReturning(HttpStatusCode status, string body) =>
        new(
            new HttpClient(new StubHttpHandler(status, body)) { BaseAddress = new Uri("http://localhost:5205/") },
            new StubTokenAcquirer(),
            NullLogger<ScribeRenderClient>.Instance
        );

    private sealed class StubHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
    }

    private sealed class StubTokenAcquirer : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("fake-m2m-token");
    }

    // ------------------------------------------------------------------
    // Consumer representativo de único destinatario (Auth) — antes: "if (render.IsFailure) return;"
    // ------------------------------------------------------------------

    [Fact]
    public async Task UserRegisteredConsumer_throws_instead_of_silently_returning_when_Scribe_render_fails()
    {
        var scribeClient = new FailingScribeRenderClient();
        var gateway = new RecordingEmailDispatchGateway();
        var evt = new UserRegisteredIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Email = "new-user@test.com",
            ActorType = "User",
            Name = "Ana",
            LastName = "Perez",
        };

        var thrown = await Assert.ThrowsAsync<ScribeRenderFailedException>(() =>
            UserRegisteredConsumer.Handle(
                evt,
                gateway,
                scribeClient,
                Options.Create(new PortalOptions()),
                new FakeTenantHostResolver(),
                new NoOpCorrelationContext(),
                CancellationToken.None
            )
        );

        Assert.Equal("auth.user_registered.v1", thrown.EventKey);
        // La prueba real de "no se pierde en silencio": el welcome email nunca se encoló.
        Assert.Empty(gateway.QueuedRequests);
    }

    // ------------------------------------------------------------------
    // Consumer representativo de único destinatario (Signature) — el mismo patrón que
    // SignatureRequestReminderDueConsumer / SignatureRequestExpiredConsumer / SignerVerificationChallengeIssuedConsumer.
    // ------------------------------------------------------------------

    [Fact]
    public async Task SignerInvitedConsumer_throws_instead_of_silently_returning_when_Scribe_render_fails()
    {
        var scribeClient = new FailingScribeRenderClient();
        var gateway = new RecordingEmailDispatchGateway();
        var evt = new SignerInvitedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            SignatureRequestId = Guid.NewGuid(),
            SignerId = Guid.NewGuid(),
            Email = "signer@test.com",
            FullName = "Signer One",
            Order = 1,
            Language = "Es",
            PublicToken = "abc",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(3),
            RevocationEpoch = 0,
            RequiresConsent = false,
            RequiresSequentialSigning = false,
        };

        await Assert.ThrowsAsync<ScribeRenderFailedException>(() =>
            SignerInvitedConsumer.Handle(
                evt,
                gateway,
                scribeClient,
                new NoOpSmsSender(),
                new NotificationDispatcherHarness().Dispatcher,
                NoPortalRecipientResolver.Instance,
                Options.Create(new PortalOptions()),
                TestPlatform.Settings,
                new FakeTenantHostResolver(),
                new NoOpCorrelationContext(),
                NullLogger<SignerInvitedIntegrationEvent>.Instance,
                CancellationToken.None
            )
        );

        Assert.Empty(gateway.QueuedRequests);
    }

    // ------------------------------------------------------------------
    // Consumer representativo de fan-out (foreach por firmante) — antes: log + "continue", que
    // dejaba a ese firmante sin notificar para siempre sin que Wolverine se enterara.
    // ------------------------------------------------------------------

    [Fact]
    public async Task SignerRejectedConsumer_throws_on_first_failed_render_instead_of_silently_continuing_the_loop()
    {
        var scribeClient = new FailingScribeRenderClient();
        var gateway = new RecordingEmailDispatchGateway();
        var evt = new SignerRejectedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            SignatureRequestId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            SignerId = Guid.NewGuid(),
            RejectedAtUtc = DateTime.UtcNow,
            RevocationEpoch = 1,
            PendingSignerIds = [Guid.NewGuid(), Guid.NewGuid()],
            PendingSigners =
            [
                new SignerContactSnapshot(Guid.NewGuid(), "pending1@test.com", "Pending One", "Es", 1),
                new SignerContactSnapshot(Guid.NewGuid(), "pending2@test.com", "Pending Two", "Es", 2),
            ],
        };

        await Assert.ThrowsAsync<ScribeRenderFailedException>(() =>
            SignerRejectedConsumer.Handle(
                evt,
                gateway,
                scribeClient,
                new NoOpCorrelationContext(),
                CancellationToken.None
            )
        );

        // Con el bug viejo, este assert habría fallado con Wolverine viendo el handler como exitoso
        // (0 excepciones) mientras 0 emails salían — ahora la falla es visible y accionable.
        Assert.Empty(gateway.QueuedRequests);
    }

    // ------------------------------------------------------------------
    // Fakes — mismo estilo hand-rolled que EventBasedEmailDispatchGatewayTests, sin librería de mocking.
    // ------------------------------------------------------------------

    private sealed class FailingScribeRenderClient : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ct);

        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => TestDoubles.ScribeTestDefaults.TextUnsupported();

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Failure<ScribeRenderedEmail>(
                    new Error("Email.ScribeRender", $"Scribe render request failed (503) for '{eventKey}'.")
                )
            );
    }

    private sealed class RecordingEmailDispatchGateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> QueuedRequests { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            QueuedRequests.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    TaxVision.Notification.Domain.Notifications.NotificationDispatchAttemptStatus.Queued,
                    ProviderMessageId: null,
                    Error: null
                )
            );
        }
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
