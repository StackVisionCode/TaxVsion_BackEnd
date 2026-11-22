using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Domain.Recipients;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// La alerta de seguridad llega por correo solo al afectado (en inglés, plantilla de alerta de Scribe) y por
/// campanita al afectado, al dueño y a los suscriptores de la oficina.
/// </summary>
public sealed class SecurityAlertConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string AffectedEmail = "ana@example.com";

    [Fact]
    public async Task Password_changed_sends_an_email_to_the_account_owner()
    {
        var context = new Context();
        var evt = Alert(SecurityAlertType.PasswordChanged);

        await context.HandleAsync(evt);

        var email = Assert.Single(context.Gateway.Requests);
        Assert.Equal(AffectedEmail, email.To);
        Assert.Equal("auth.security_alert", email.TemplateKey);
        Assert.Equal(evt.EventId, email.RelatedEventId);
        Assert.Equal(evt.UserId, email.RecipientUserId);
        Assert.Equal(NotificationCategory.AccountSecurity, email.Category);
        Assert.Equal(NotificationKinds.AuthSecurityAlert, email.NotificationKind);
        Assert.Null(email.IdempotencyKey);
        Assert.Equal("auth.email_change_security_alert.v1", context.Scribe.LastEventKey);
        Assert.Equal(1, context.Gateway.Sent);
    }

    [Theory]
    [InlineData(SecurityAlertType.PasswordChanged, "The password of your TaxOffix workspace account was changed.")]
    [InlineData(
        SecurityAlertType.MfaDisabled,
        "Two-step verification was turned off on your TaxOffix workspace account."
    )]
    [InlineData(
        SecurityAlertType.AccountLockedOut,
        "Your TaxOffix workspace account was locked after several failed sign-in attempts."
    )]
    [InlineData(
        SecurityAlertType.TokenReuseDetected,
        "We detected unusual session activity on your TaxOffix workspace account and signed you out everywhere."
    )]
    [InlineData(
        SecurityAlertType.RefreshTokenHostMismatch,
        "A session of your TaxOffix workspace account was used from an unexpected address and was closed."
    )]
    [InlineData(SecurityAlertType.EmailChanged, "The email address of your TaxOffix workspace account was changed.")]
    [InlineData("something_new", "We noticed important security activity on your TaxOffix workspace account.")]
    public async Task The_description_is_in_English_for_each_alert_type(string alertType, string description)
    {
        var context = new Context();

        await context.HandleAsync(Alert(alertType) with { IpAddress = "203.0.113.7" });

        Assert.Equal(description, context.Scribe.LastVariables["description"]);
        Assert.Equal("203.0.113.7", context.Scribe.LastVariables["ip_address"]);
    }

    [Fact]
    public async Task The_in_app_title_is_in_English()
    {
        var context = new Context();

        await context.HandleAsync(Alert(SecurityAlertType.PasswordChanged));

        Assert.All(
            context.InApp(),
            notification => Assert.Equal("Security alert on your TaxOffix account", notification.FallbackTitle)
        );
    }

    [Fact]
    public async Task Email_changed_goes_to_the_previous_address()
    {
        var context = new Context();

        await context.HandleAsync(
            Alert(SecurityAlertType.EmailChanged) with
            {
                DetailsJson = """{"previousEmail":"old@x.com"}""",
            }
        );

        Assert.Equal("old@x.com", Assert.Single(context.Gateway.Requests).To);
    }

    [Fact]
    public async Task A_retry_does_not_send_twice()
    {
        var context = new Context();
        var evt = Alert(SecurityAlertType.AccountLockedOut);

        await context.HandleAsync(evt);
        await context.HandleAsync(evt);

        Assert.Equal(1, context.Gateway.Sent);
    }

    [Fact]
    public async Task Office_subscribers_get_the_bell_but_not_the_email()
    {
        var context = new Context();
        var evt = Alert(SecurityAlertType.MfaDisabled);

        await context.HandleAsync(evt);

        Assert.Equal(
            new[] { evt.UserId, context.Owner, context.Subscriber }.Order(),
            context.InApp().Select(notification => notification.RecipientUserId).Order()
        );
        Assert.Equal(evt.UserId, Assert.Single(context.Gateway.Requests).RecipientUserId);
    }

    [Fact]
    public async Task A_user_without_a_known_address_still_gets_the_bell()
    {
        var context = new Context(knownEmail: null);
        var evt = Alert(SecurityAlertType.PasswordChanged);

        await context.HandleAsync(evt);

        Assert.Empty(context.Gateway.Requests);
        Assert.Contains(evt.UserId, context.InApp().Select(notification => notification.RecipientUserId));
    }

    [Fact]
    public async Task A_portal_user_is_rendered_for_the_client()
    {
        var userId = Guid.NewGuid();
        var context = new Context(portalUserId: userId);

        await context.HandleAsync(Alert(SecurityAlertType.PasswordChanged) with { UserId = userId });

        Assert.Equal(ScribeRenderAudience.Client, context.Scribe.LastAudience);
        Assert.Equal(
            "The password of your client portal account was changed.",
            context.Scribe.LastVariables["description"]
        );
    }

    private static SecurityAlertIntegrationEvent Alert(string alertType) =>
        new()
        {
            TenantId = TenantId,
            UserId = Guid.NewGuid(),
            AlertType = alertType,
        };

    /// <summary>La alerta llega en el idioma del afectado, con la frase del sistema también en su idioma.</summary>
    [Fact]
    public async Task Email_is_rendered_with_the_recipient_language()
    {
        var context = new Context(locales: new FixedRecipientLocale("es"));

        await context.HandleAsync(Alert(SecurityAlertType.PasswordChanged));

        Assert.Equal("Es", context.Scribe.LastVariables["language"]);
        Assert.Equal(
            $"Se cambió la contraseña de tu cuenta de {TestPlatform.Settings.Current.BrandName}.",
            context.Scribe.LastVariables["description"]
        );
    }

    private sealed class Context(
        string? knownEmail = AffectedEmail,
        Guid? portalUserId = null,
        IRecipientLocale? locales = null
    )
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Subscriber { get; } = Guid.NewGuid();
        private NotificationDispatcherHarness? harness;

        public DedupingEmailGateway Gateway { get; } = new();

        // El correo sale por el punto único, que encola en este gateway.
        public NotificationDispatcherHarness Harness => harness ??= new(emailGateway: Gateway);
        public AudienceScribe Scribe { get; } = new();

        public Task HandleAsync(SecurityAlertIntegrationEvent evt) =>
            SecurityAlertConsumer.Handle(
                evt,
                Harness.Dispatcher,
                Scribe,
                new FixedUserEmails(knownEmail),
                new PortalAccountsStub(portalUserId),
                Options.Create(new PortalOptions()),
                TestPlatform.Settings,
                new OfficeResolver(Owner, Subscriber),
                locales ?? FixedRecipientLocale.English,
                new TestCorrelationContext(),
                NullLogger<SecurityAlertIntegrationEvent>.Instance,
                CancellationToken.None
            );

        public IEnumerable<InAppNotificationRequestedIntegrationEvent> InApp() =>
            Harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>();
    }

    // Gateway real con un log que deduplica como el repositorio de SQL.
    private sealed class DedupingEmailGateway : IEmailDispatchGateway
    {
        private readonly CapturingIntegrationEventPublisher _publisher = new();
        private readonly EventBasedEmailDispatchGateway _inner;

        public DedupingEmailGateway() =>
            _inner = new(
                _publisher,
                new DedupingLogs(),
                new NoOpUnitOfWork(),
                TestDeliveryGates.Open(),
                NullLogger<EventBasedEmailDispatchGateway>.Instance
            );

        public List<EmailDispatchRequest> Requests { get; } = [];

        public int Sent =>
            _publisher.Published.OfType<NotificationsEmailSendRequestedIntegrationEvent>().Count()
            + _publisher.SentToQueue.Count(sent => sent.Message is NotificationsEmailSendRequestedIntegrationEvent);

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return _inner.QueueEmailAsync(request, ct);
        }
    }

    private sealed class DedupingLogs : INotificationLogRepository
    {
        private readonly List<NotificationLog> _logs = [];

        public Task AddAsync(NotificationLog log, CancellationToken ct = default)
        {
            _logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<NotificationLog?> GetByRelatedEventIdAsync(
            Guid tenantId,
            Guid relatedEventId,
            string templateKey,
            string recipient,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _logs.FirstOrDefault(log =>
                    log.TenantId == tenantId
                    && log.RelatedEventId == relatedEventId
                    && log.TemplateKey == templateKey
                    && log.Recipient == recipient
                )
            );

        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>((_logs, _logs.Count));
    }

    private sealed class NoOpUnitOfWork : BuildingBlocks.Persistence.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class AudienceScribe : IScribeRenderClient
    {
        public string? LastEventKey { get; private set; }
        public ScribeRenderAudience? LastAudience { get; private set; }
        public IReadOnlyDictionary<string, object?> LastVariables { get; private set; } =
            new Dictionary<string, object?>();

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
            LastEventKey = eventKey;
            LastAudience = audience;
            LastVariables = variables;
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("subject", "<p>body</p>", "body")));
        }

        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => ScribeTestDefaults.TextUnsupported();
    }

    private sealed class OfficeResolver(Guid owner, Guid subscriber) : IRecipientResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([subscriber]);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success<IReadOnlyList<Guid>>(
                    audience switch
                    {
                        TenantOwnerAudience => [owner],
                        ByPermission => [subscriber],
                        _ => [],
                    }
                )
            );
    }
}

/// <summary>Resolvedor de correo fijo: la misma dirección (o ninguna) para cualquier usuario.</summary>
internal sealed class FixedUserEmails(string? email) : IUserEmailResolver
{
    public Task<string?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(email);
}

/// <summary>Proyección de cuentas de portal con, a lo sumo, un usuario de portal.</summary>
internal sealed class PortalAccountsStub(Guid? portalUserId) : ICustomerPortalAccountProjectionRepository
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
