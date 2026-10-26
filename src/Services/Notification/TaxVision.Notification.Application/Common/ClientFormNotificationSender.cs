using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

public sealed record ClientFormNotificationRequest(
    Guid TenantId,
    Guid? RecipientUserId,
    Guid FormRequestId,
    Guid EventId,
    DateTime OccurredOn,
    string Kind,
    string EmailTemplateKey,
    string PushTemplateKey,
    string Title,
    string Body,
    IReadOnlyDictionary<string, object?> Variables,
    IReadOnlyDictionary<string, string> Params,
    NotificationCategory Category,
    Guid? CustomerId = null,
    bool CustomerRecipient = false
);

public sealed record ClientFormExternalEmailRequest(
    Guid TenantId,
    Guid EventId,
    string Kind,
    string TemplateKey,
    string Email,
    string Language,
    IReadOnlyDictionary<string, object?> Variables,
    bool Confidential = true
);

public sealed record RetryClientFormPushRequested(PushDispatchRequest Request, int Attempt);

public sealed class ClientFormNotificationSender(
    ICustomerEmailDirectoryRepository customers,
    ICustomerContactPolicyRepository contactPolicies,
    IUserEmailResolver userEmails,
    IUserNotificationPreferenceRepository preferences,
    IClientFormNotificationSettingsRepository settings,
    IEmailDispatchGateway emailGateway,
    IScribeRenderClient scribe,
    NotificationDispatcher dispatcher,
    ITenantHostResolver hosts,
    IOptions<PortalOptions> portal,
    INotificationDispatchMetrics metrics,
    ICorrelationContext correlation,
    IClientFormPushRetryScheduler retryScheduler,
    ILogger<ClientFormNotificationSender> logger
)
{
    // Null cuando el catálogo no permite email para el kind.
    public async Task<EmailDispatchResult?> SendExternalEmailAsync(
        ClientFormExternalEmailRequest request,
        CancellationToken ct
    )
    {
        var entry = NotificationCatalog.GetRequired(request.Kind);
        if (!entry.AllowedChannels.Contains(NotificationChannel.Email))
            return null;
        var variables = new Dictionary<string, object?>(request.Variables) { ["language"] = request.Language };
        var render = (await scribe.RenderAsync(request.TemplateKey, request.TenantId, variables, ct)).EnsureRendered(
            request.TemplateKey
        );
        var dispatch = render.ToDispatchRequest(
            request.TenantId,
            request.Email,
            request.TemplateKey,
            request.EventId,
            correlation.CorrelationId
        ) with
        {
            IdempotencyKey = request.EventId.ToString("N"),
            Confidential = request.Confidential,
        };
        return await emailGateway.QueueEmailAsync(dispatch, ct);
    }

    public async Task SendAsync(ClientFormNotificationRequest request, CancellationToken ct)
    {
        var entry = NotificationCatalog.GetRequired(request.Kind);
        var sendsEmail = entry.AllowedChannels.Contains(NotificationChannel.Email);
        var sendsPush = entry.AllowedChannels.Contains(NotificationChannel.Push);
        // Un kind solo in-app no consulta destinatario ni host.
        var destination = sendsEmail || sendsPush ? await ResolveDestinationAsync(request, sendsEmail, ct) : null;
        var host = sendsEmail || sendsPush ? await hosts.ResolveHostAsync(request.TenantId, ct) : null;

        if (sendsEmail)
            await SendEmailAsync(request, entry, destination!, host, ct);
        if (entry.AllowedChannels.Contains(NotificationChannel.InApp))
            await SendInAppAsync(request, ct);
        if (sendsPush)
            await SendPushAsync(request, destination!.Language, host, ct);
    }

    private async Task<Destination> ResolveDestinationAsync(
        ClientFormNotificationRequest request,
        bool needsEmail,
        CancellationToken ct
    )
    {
        if (!request.CustomerRecipient || request.CustomerId is not { } customerId)
            return new(
                needsEmail && request.RecipientUserId is { } userId
                    ? await userEmails.ResolveAsync(request.TenantId, userId, ct)
                    : null,
                "En",
                true,
                null
            );

        var customer = needsEmail ? await customers.GetByCustomerIdAsync(request.TenantId, customerId, ct) : null;
        var policy = await contactPolicies.GetByCustomerIdAsync(request.TenantId, customerId, ct);
        return new(
            customer is { IsActive: true } ? customer.NormalizedEmail : null,
            policy?.Language ?? "En",
            policy?.EmailEnabled ?? true,
            customer?.DisplayName
        );
    }

    private async Task SendEmailAsync(
        ClientFormNotificationRequest request,
        NotificationCatalogEntry entry,
        Destination destination,
        string? host,
        CancellationToken ct
    )
    {
        if (!destination.EmailAllowed)
        {
            metrics.RecordNoRecipient(request.Kind, "Email");
            return;
        }
        if (
            !entry.IsMandatoryFor(NotificationChannel.Email)
            && !await IsPreferredAsync(request, NotificationChannel.Email, ct)
        )
            return;
        if (string.IsNullOrWhiteSpace(destination.Email))
        {
            metrics.RecordNoRecipient(request.Kind, "Email");
            return;
        }

        var variables = Variables(request, destination, host);
        var render = (
            await scribe.RenderAsync(request.EmailTemplateKey, request.TenantId, variables, ct)
        ).EnsureRendered(request.EmailTemplateKey);
        await emailGateway.QueueEmailAsync(
            render.ToDispatchRequest(
                request.TenantId,
                destination.Email,
                request.EmailTemplateKey,
                request.EventId,
                correlation.CorrelationId
            ),
            ct
        );
    }

    private async Task SendInAppAsync(ClientFormNotificationRequest request, CancellationToken ct)
    {
        if (request.RecipientUserId is not { } userId)
        {
            metrics.RecordNoRecipient(request.Kind, "InApp");
            return;
        }

        await dispatcher.RecordInAppAsync(
            request.TenantId,
            $"user:{userId:N}",
            request.Title,
            request.Category,
            request.Kind,
            request.EventId,
            correlation.CorrelationId,
            recipientUserId: userId,
            inApp: BuildInApp(request, userId),
            ct: ct
        );
    }

    private async Task SendPushAsync(
        ClientFormNotificationRequest request,
        string language,
        string? host,
        CancellationToken ct
    )
    {
        if (request.RecipientUserId is not { } userId)
        {
            metrics.RecordNoRecipient(request.Kind, "Push");
            return;
        }
        if (!await settings.IsPushEnabledAsync(request.TenantId, ct))
        {
            metrics.RecordNoRecipient(request.Kind, "Push");
            return;
        }
        // Gates de preferencia y dispositivos antes de pedir el texto a Scribe.
        if (!await dispatcher.CanSendPushAsync(request.TenantId, userId, request.Category, request.Kind, ct))
            return;

        var rendered = await RenderPushAsync(request, language, ct);
        if (rendered is null)
            return;
        var identifiers = PushIdentifiers(request.EventId);
        var pushRequest = new PushDispatchRequest(
            request.TenantId,
            userId,
            rendered.Subject,
            rendered.Text,
            request.Category,
            request.Kind,
            request.PushTemplateKey,
            request.EventId,
            correlation.CorrelationId,
            PushData(request),
            identifiers.Topic,
            identifiers.Tag,
            PushLink(request, host),
            SkipWhenNoDevices: true
        );
        await ScheduleRetryAsync(pushRequest, await DispatchPushAsync(pushRequest, ct), 0);
    }

    // El push nunca lanza hacia el emisor. Null = excepción inesperada: se reintenta el mismo envío completo.
    private async Task<PushDispatchOutcome?> DispatchPushAsync(PushDispatchRequest request, CancellationToken ct)
    {
        try
        {
            return await dispatcher.DispatchPushAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Client form push {Kind} for event {EventId} failed unexpectedly; it will be retried.",
                request.Kind,
                request.RelatedEventId
            );
            return null;
        }
    }

    // Solo el email puede lanzar (P-21.3): un fallo del push se registra y no revierte email ni in-app.
    private async Task<ScribeRenderedText?> RenderPushAsync(
        ClientFormNotificationRequest request,
        string language,
        CancellationToken ct
    )
    {
        var variables = new Dictionary<string, object?> { ["language"] = language };
        if (request.Variables.TryGetValue("is_overdue", out var isOverdue))
            variables["is_overdue"] = isOverdue;
        Result<ScribeRenderedText> rendered;
        try
        {
            rendered = await scribe.RenderTextAsync(request.PushTemplateKey, request.TenantId, variables, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            rendered = Result.Failure<ScribeRenderedText>(new Error("Notification.ScribeRender", ex.Message));
        }
        if (rendered.IsSuccess)
            return rendered.Value;
        metrics.RecordClientFormPushFailure("render_failed");
        logger.LogWarning(
            "Client form push {Kind} for event {EventId} skipped: template {TemplateKey} failed to render ({Error}).",
            request.Kind,
            request.EventId,
            request.PushTemplateKey,
            rendered.Error.Code
        );
        return null;
    }

    public async Task RetryPushAsync(RetryClientFormPushRequested retry, CancellationToken ct)
    {
        if (!await settings.IsPushEnabledAsync(retry.Request.TenantId, ct))
            return;
        await ScheduleRetryAsync(retry.Request, await DispatchPushAsync(retry.Request, ct), retry.Attempt);
    }

    // Reintenta solo los dispositivos con fallo transitorio; los que ya recibieron no se repiten.
    private async Task ScheduleRetryAsync(PushDispatchRequest request, PushDispatchOutcome? outcome, int attempt)
    {
        foreach (var code in outcome?.FailureCodes ?? [PushErrorCodes.Unavailable])
            metrics.RecordClientFormPushFailure(code);
        if (outcome is { RetryableDeviceIds.Count: 0 } || attempt >= 3)
            return;
        var delays = new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15) };
        var next = outcome is null ? request : request with { DeviceIds = outcome.RetryableDeviceIds };
        await retryScheduler.ScheduleAsync(new RetryClientFormPushRequested(next, attempt + 1), delays[attempt]);
    }

    private async Task<bool> IsPreferredAsync(
        ClientFormNotificationRequest request,
        NotificationChannel channel,
        CancellationToken ct
    )
    {
        // Sin cuenta no hay opt-out que respetar: el cliente sin portal recibe el email (lo filtra su ContactPolicy).
        if (request.RecipientUserId is not { } userId)
            return true;
        var enabled = await preferences.IsEnabledAsync(request.TenantId, userId, request.Category, channel, ct);
        if (!enabled)
            metrics.RecordSuppressedByPreference(request.Kind, channel.ToString());
        return enabled;
    }

    private Dictionary<string, object?> Variables(
        ClientFormNotificationRequest request,
        Destination destination,
        string? host
    )
    {
        var variables = new Dictionary<string, object?>(request.Variables)
        {
            ["language"] = destination.Language,
            ["portal_link"] = TenantEmailLinks.ClientForm(host, portal.Value, request.FormRequestId),
            ["staff_link"] =
                $"{TenantEmailLinks.StaffBase(host, portal.Value)}/client-forms?formRequestId={request.FormRequestId:D}",
        };
        // Al cliente se le saluda con su nombre del directorio; al staff le llega el del consumer.
        if (destination.DisplayName is not null)
            variables["customer_name"] = destination.DisplayName;
        return variables;
    }

    private static InAppNotificationRequest BuildInApp(ClientFormNotificationRequest request, Guid userId)
    {
        var context = new Dictionary<string, string> { ["formRequestId"] = request.FormRequestId.ToString() };
        if (request.CustomerId is { } customerId)
            context["customerId"] = customerId.ToString();
        return new(
            request.Kind,
            userId,
            request.EventId,
            request.EmailTemplateKey,
            request.OccurredOn,
            request.Title,
            FallbackBody: request.Body,
            ResourceId: request.FormRequestId.ToString(),
            ResourceContext: context,
            Params: request.Params,
            GroupKey: $"form_request:{request.FormRequestId:N}"
        );
    }

    private static IReadOnlyDictionary<string, string> PushData(ClientFormNotificationRequest request)
    {
        var data = new Dictionary<string, string>
        {
            ["kind"] = request.Kind,
            ["resourceType"] = "form_request",
            ["resourceId"] = request.FormRequestId.ToString(),
        };
        if (request.CustomerId is { } customerId)
            data["customerId"] = customerId.ToString();
        return data;
    }

    // El cliente abre /n/open en el portal; el staff, en el CRM.
    private string PushLink(ClientFormNotificationRequest request, string? host)
    {
        var baseUrl = request.CustomerRecipient
            ? TenantEmailLinks.ClientBase(host, portal.Value)
            : TenantEmailLinks.StaffBase(host, portal.Value);
        var link = $"{baseUrl}/n/open?rt=form_request&rid={request.FormRequestId:D}";
        return request.CustomerId is { } customerId ? $"{link}&cid={customerId:D}" : link;
    }

    internal static (string Topic, string Tag) PushIdentifiers(Guid eventId)
    {
        var hash = SHA256.HashData(eventId.ToByteArray());
        var value = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (value[..32], $"cf-{value[..22]}");
    }

    private sealed record Destination(string? Email, string Language, bool EmailAllowed, string? DisplayName);
}
