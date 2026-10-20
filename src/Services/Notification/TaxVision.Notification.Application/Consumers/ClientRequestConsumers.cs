using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// <c>task.client_request_created.v1</c> — la firma le pidió algo al cliente y hay que decírselo.
///
/// <para>
/// El destinatario es el cliente, se le avisa por DOS canales: email (via directorio de clientes)
/// e in-app en el portal (si tiene cuenta de portal activa, resuelta via
/// <see cref="CustomerPortal"/>). Si no tiene portal se queda solo con email; si no tiene email
/// se queda solo con in-app. Si no tiene ninguno de los dos se omite en silencio.
/// </para>
/// </summary>
public static class ClientRequestCreatedConsumer
{
    private const string TemplateKey = "task.client_request_created.v1";

    public static async Task Handle(
        ClientRequestCreatedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository customers,
        ICustomerContactPolicyRepository contactPolicies,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var customer = await customers.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);

            // Email al cliente (si tiene direccion resoluble Y la política por canal lo permite).
            // In-app sigue siempre: no es un canal del VO, el portal lo pinta cuando el cliente entra.
            var emailAllowed = await CustomerContactPolicyFilter.AllowsAsync(
                evt.TenantId,
                evt.CustomerId,
                CustomerContactChannel.Email,
                contactPolicies,
                ct
            );
            if (emailAllowed && customer is { IsActive: true } && customer.NormalizedEmail.Length > 0)
            {
                // El cliente entra a SU oficina: link al portal bajo el subdominio del tenant, no a un base fijo.
                var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);

                var render = (
                    await scribeClient.RenderAsync(
                        TemplateKey,
                        evt.TenantId,
                        new Dictionary<string, object?>
                        {
                            ["customer_name"] = customer.DisplayName,
                            ["request_title"] = evt.Title,
                            ["request_details"] = evt.Details,
                            ["due_at_utc"] = evt.DueAtUtc,
                            ["portal_link"] = TenantEmailLinks.ClientDocuments(tenantHost, portal.Value),
                        },
                        ct
                    )
                ).EnsureRendered(TemplateKey);

                await gateway.QueueEmailAsync(
                    render.ToDispatchRequest(
                        tenantId: evt.TenantId,
                        to: customer.NormalizedEmail,
                        templateKey: TemplateKey,
                        relatedEventId: evt.EventId,
                        correlationId: correlation.CorrelationId
                    ),
                    ct
                );
            }

            // In-app al cliente en el portal (si tiene cuenta activa).
            var portalUserId = await CustomerPortalRecipients.ResolveActivePortalUserAsync(
                evt.TenantId,
                evt.CustomerId,
                recipientResolver,
                ct
            );
            if (portalUserId is { } userId)
            {
                var title = $"Nueva solicitud: {evt.Title}";
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    $"user:{userId:N}",
                    title,
                    NotificationCategory.DocumentsAndSignatures,
                    "task.client_request_created",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.TasksClientRequestCreated,
                        userId,
                        evt.EventId,
                        "tasks.client_request_created.v1",
                        evt.OccurredOn,
                        title,
                        FallbackBody: evt.Details,
                        ResourceId: evt.ClientRequestId.ToString(),
                        ResourceContext: new Dictionary<string, string> { ["customerId"] = evt.CustomerId.ToString() },
                        Params: new Dictionary<string, string> { ["title"] = evt.Title }
                    ),
                    ct: ct
                );
            }
        }
    }
}

/// <summary>
/// <c>task.client_request_fulfilled.v1</c> — el cliente mandó algo.
///
/// <para>
/// Va sólo a quien lo pidió, con su id: es seguimiento personal, no una alerta de la firma. Mandarlo
/// a todo el que tenga <c>tasks.read</c> en una oficina de treinta es ruido, y el ruido se silencia
/// justo donde importa.
/// </para>
/// </summary>
public static class ClientRequestFulfilledConsumer
{
    public static async Task Handle(
        ClientRequestFulfilledIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var title = $"El cliente respondió: {evt.Title}";
            var body = evt.DocumentCount == 1 ? "Subió un documento." : $"Subió {evt.DocumentCount} documentos.";

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.RequestedByUserId:N}",
                title,
                NotificationCategory.DocumentsAndSignatures,
                "task.client_request_fulfilled",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.RequestedByUserId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.TasksClientRequestFulfilled,
                    evt.RequestedByUserId,
                    evt.EventId,
                    "tasks.client_request_fulfilled.v1",
                    evt.OccurredOn,
                    title,
                    FallbackBody: body,
                    ResourceId: evt.ClientRequestId.ToString(),
                    ResourceContext: new Dictionary<string, string> { ["customerId"] = evt.CustomerId.ToString() },
                    Params: new Dictionary<string, string>
                    {
                        ["title"] = evt.Title,
                        ["documentCount"] = evt.DocumentCount.ToString(),
                    }
                ),
                ct: ct
            );

            await dispatcher.SendPushAsync(
                evt.TenantId,
                evt.RequestedByUserId,
                title,
                body,
                NotificationCategory.DocumentsAndSignatures,
                "task.client_request_fulfilled",
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}

/// <summary>
/// <c>task.client_request_document_rejected.v1</c> — el escaneo tumbó lo que subió el cliente.
///
/// <para>
/// <b>Dos avisos con dos textos distintos.</b> Al cliente le llega por correo el mensaje accionable
/// («no pudimos procesarlo, volvé a subirlo»); al preparador, in-app con el motivo real. Decirle al
/// cliente que su archivo tiene un virus no le indica qué hacer y expone detalle de infraestructura;
/// ocultárselo del todo lo dejaría esperando por algo que nunca va a llegar.
/// </para>
/// </summary>
public static class ClientRequestDocumentRejectedConsumer
{
    private const string TemplateKey = "task.client_request_document_rejected.v1";

    public static async Task Handle(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository customers,
        ICustomerContactPolicyRepository contactPolicies,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await NotifyPreparerAsync(evt, dispatcher, correlation, ct);
            await NotifyCustomerAsync(
                evt,
                customers,
                contactPolicies,
                gateway,
                scribeClient,
                portal,
                hostResolver,
                correlation,
                ct
            );
            await NotifyCustomerPortalAsync(evt, dispatcher, recipientResolver, correlation, ct);
        }
    }

    /// <summary>
    /// In-app en el portal al cliente cuando el escaneo tumba su archivo. Es el canal
    /// paralelo al email para el cliente — el email le dice que lo re-suba, el in-app lo
    /// lleva derechito a la solicitud en el portal.
    /// </summary>
    private static async Task NotifyCustomerPortalAsync(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var portalUserId = await CustomerPortalRecipients.ResolveActivePortalUserAsync(
            evt.TenantId,
            evt.CustomerId,
            recipientResolver,
            ct
        );
        if (portalUserId is not { } userId)
            return;

        var title = $"Tu archivo necesita atencion: {evt.DisplayName}";
        await dispatcher.RecordInAppAsync(
            evt.TenantId,
            $"user:{userId:N}",
            title,
            NotificationCategory.DocumentsAndSignatures,
            "tasks.client_request_document_rejected",
            evt.EventId,
            correlation.CorrelationId,
            recipientUserId: userId,
            inApp: new InAppNotificationRequest(
                NotificationKinds.TasksClientRequestDocumentRejected,
                userId,
                evt.EventId,
                "tasks.client_request_document_rejected.v1",
                evt.OccurredOn,
                title,
                FallbackBody: evt.ClientMessage,
                ResourceId: evt.ClientRequestId.ToString(),
                ResourceContext: new Dictionary<string, string> { ["customerId"] = evt.CustomerId.ToString() },
                Params: new Dictionary<string, string> { ["displayName"] = evt.DisplayName }
            ),
            ct: ct
        );
    }

    private static async Task NotifyPreparerAsync(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var title = $"Archivo del cliente rechazado: {evt.DisplayName} ({evt.Reason})";
        await dispatcher.RecordInAppAsync(
            evt.TenantId,
            $"user:{evt.RequestedByUserId:N}",
            title,
            NotificationCategory.DocumentsAndSignatures,
            "task.client_request_document_rejected",
            evt.EventId,
            correlation.CorrelationId,
            recipientUserId: evt.RequestedByUserId,
            inApp: new InAppNotificationRequest(
                NotificationKinds.TasksClientRequestDocumentRejected,
                evt.RequestedByUserId,
                evt.EventId,
                "tasks.client_request_document_rejected.v1",
                evt.OccurredOn,
                title,
                FallbackBody: $"Motivo: {evt.Reason}.",
                ResourceId: evt.ClientRequestId.ToString(),
                ResourceContext: new Dictionary<string, string> { ["customerId"] = evt.CustomerId.ToString() },
                Params: new Dictionary<string, string> { ["displayName"] = evt.DisplayName, ["reason"] = evt.Reason }
            ),
            ct: ct
        );
    }

    private static async Task NotifyCustomerAsync(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository customers,
        ICustomerContactPolicyRepository contactPolicies,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (
            !await CustomerContactPolicyFilter.AllowsAsync(
                evt.TenantId,
                evt.CustomerId,
                CustomerContactChannel.Email,
                contactPolicies,
                ct
            )
        )
            return;

        var customer = await customers.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
        if (customer is null || !customer.IsActive || customer.NormalizedEmail.Length == 0)
            return;

        var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);

        var render = (
            await scribeClient.RenderAsync(
                TemplateKey,
                evt.TenantId,
                new Dictionary<string, object?>
                {
                    ["customer_name"] = customer.DisplayName,
                    ["document_name"] = evt.DisplayName,
                    ["request_title"] = evt.DisplayName,
                    ["client_message"] = evt.ClientMessage,
                    ["portal_link"] = TenantEmailLinks.ClientDocuments(tenantHost, portal.Value),
                },
                ct
            )
        ).EnsureRendered(TemplateKey);

        await gateway.QueueEmailAsync(
            render.ToDispatchRequest(
                tenantId: evt.TenantId,
                to: customer.NormalizedEmail,
                templateKey: TemplateKey,
                relatedEventId: evt.EventId,
                correlationId: correlation.CorrelationId
            ),
            ct
        );
    }
}
