using System.Text.Json;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

// Alerta de seguridad: primero el correo, solo al afectado (en email_changed, a la dirección anterior); después
// la campanita al afectado, al dueño y a los suscriptores de la oficina.
public static class SecurityAlertConsumer
{
    private const string EmailEventKey = "auth.email_change_security_alert.v1";

    public static async Task Handle(
        SecurityAlertIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IScribeRenderClient scribeClient,
        IUserEmailResolver emails,
        ICustomerPortalAccountProjectionRepository portalAccounts,
        IOptions<PortalOptions> portal,
        IPlatformSettings platform,
        IRecipientResolver recipientResolver,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        ILogger<SecurityAlertIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var brand = platform.Current.BrandName;
            var to = PreviousEmail(evt) ?? await emails.ResolveAsync(evt.TenantId, evt.UserId, ct);
            if (string.IsNullOrWhiteSpace(to))
                logger.LogWarning(
                    "Security alert {AlertType} for user {UserId} in tenant {TenantId} has no email address; only the in-app notice is sent.",
                    evt.AlertType,
                    evt.UserId,
                    evt.TenantId
                );
            else
                await SendEmailAsync(
                    evt,
                    to,
                    brand,
                    dispatcher,
                    scribeClient,
                    portalAccounts,
                    await locales.ForUserAsync(evt.TenantId, evt.UserId, ct),
                    correlation.CorrelationId,
                    ct
                );

            await NotifyInAppAsync(evt, brand, dispatcher, recipientResolver, correlation.CorrelationId, ct);
        }
    }

    private static async Task SendEmailAsync(
        SecurityAlertIntegrationEvent evt,
        string to,
        string brand,
        NotificationDispatcher dispatcher,
        IScribeRenderClient scribeClient,
        ICustomerPortalAccountProjectionRepository portalAccounts,
        RecipientLocale locale,
        string? correlationId,
        CancellationToken ct
    )
    {
        // El evento no trae el tipo de actor: el usuario es cliente si es el de una cuenta de portal.
        var audience = await portalAccounts.GetByUserAsync(evt.TenantId, evt.UserId, ct) is not null
            ? ScribeRenderAudience.Client
            : ScribeRenderAudience.Staff;
        var render = (
            await scribeClient.RenderForRecipientAsync(
                EmailEventKey,
                evt.TenantId,
                new Dictionary<string, object?>
                {
                    ["description"] = Describe(evt.AlertType, audience, brand, locale),
                    ["ip_address"] = evt.IpAddress,
                },
                audience,
                locale,
                ct
            )
        ).EnsureRendered(EmailEventKey);

        await dispatcher.DispatchAsync(
            Intent(evt, new NotificationTarget(evt.UserId, to, Locale: locale), correlationId) with
            {
                Email = new EmailContent(
                    EmailEventKey,
                    new Dictionary<string, object?>(),
                    audience,
                    TemplateKey: NotificationKinds.AuthSecurityAlert,
                    Render: (_, _) => Task.FromResult(render)
                ),
            },
            ct
        );
    }

    private static async Task NotifyInAppAsync(
        SecurityAlertIntegrationEvent evt,
        string brand,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        string? correlationId,
        CancellationToken ct
    )
    {
        var subject = $"Security alert on your {brand} account";
        var officeRecipients = await OfficeAlertRecipients.ResolveOwnerAndSubscribersAsync(
            evt.TenantId,
            NotificationPermissions.SecurityTenantAlerts,
            new RecipientValidation(RequiredCapability: SubscriptionPermissions.AuditView),
            recipientResolver,
            ct
        );
        var recipients = new[] { evt.UserId }.Concat(officeRecipients).Distinct();

        foreach (var userId in recipients)
        {
            await dispatcher.DispatchAsync(
                Intent(evt, new NotificationTarget(userId), correlationId) with
                {
                    InApp = new InAppContent(
                        Params: new Dictionary<string, string> { ["alertType"] = evt.AlertType },
                        ActorUserId: evt.UserId,
                        FallbackTitle: subject
                    ),
                },
                ct
            );
        }
    }

    private static NotificationIntent Intent(
        SecurityAlertIntegrationEvent evt,
        NotificationTarget target,
        string? correlationId
    ) =>
        new(
            NotificationKinds.AuthSecurityAlert,
            evt.TenantId,
            target,
            evt.EventId,
            "auth.security_alert.v1",
            evt.OccurredOn,
            correlationId
        );

    // En email_changed Auth manda la dirección anterior en DetailsJson; si no se puede leer, se usa el directorio.
    private static string? PreviousEmail(SecurityAlertIntegrationEvent evt)
    {
        if (evt.AlertType != SecurityAlertType.EmailChanged || string.IsNullOrWhiteSpace(evt.DetailsJson))
            return null;
        try
        {
            using var details = JsonDocument.Parse(evt.DetailsJson);
            var previous =
                details.RootElement.TryGetProperty("previousEmail", out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            return string.IsNullOrWhiteSpace(previous) ? null : previous;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // La frase del sistema, en el idioma de quien lee.
    private static string Describe(
        string alertType,
        ScribeRenderAudience audience,
        string brand,
        RecipientLocale locale
    )
    {
        var account = EmailText.For(
            audience == ScribeRenderAudience.Client ? "auth.account.client" : "auth.account.staff",
            locale,
            new Dictionary<string, string> { ["brand"] = brand }
        );
        var key = alertType switch
        {
            SecurityAlertType.PasswordChanged => "auth.security_alert.password_changed",
            SecurityAlertType.MfaDisabled => "auth.security_alert.mfa_disabled",
            SecurityAlertType.AccountLockedOut => "auth.security_alert.account_locked_out",
            SecurityAlertType.TokenReuseDetected => "auth.security_alert.token_reuse_detected",
            SecurityAlertType.RefreshTokenHostMismatch => "auth.security_alert.refresh_token_host_mismatch",
            SecurityAlertType.EmailChanged => "auth.security_alert.email_changed",
            _ => "auth.security_alert.other",
        };
        return EmailText.For(
            key,
            locale,
            new Dictionary<string, string>
            {
                ["account"] = account,
                ["accountStart"] = $"{char.ToUpperInvariant(account[0])}{account[1..]}",
            }
        );
    }
}
