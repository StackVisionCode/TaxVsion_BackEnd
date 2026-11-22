using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Catalog;

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

                // Se renderiza en el idioma del cliente (el destino lleva su id).
                await dispatcher.DispatchAsync(
                    Intent(
                        evt,
                        new NotificationTarget(Email: customer.NormalizedEmail, CustomerId: evt.CustomerId),
                        correlation
                    ) with
                    {
                        Email = new EmailContent(
                            TemplateKey,
                            new Dictionary<string, object?>
                            {
                                ["customer_name"] = customer.DisplayName,
                                ["request_title"] = evt.Title,
                                ["request_details"] = evt.Details,
                                ["due_at_utc"] = CivilDue.Typed(evt.DueOn, evt.DueAtUtc),
                                ["portal_link"] = TenantEmailLinks.ClientDocuments(tenantHost, portal.Value),
                            },
                            ScribeRenderAudience.Client
                        ),
                    },
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
                await dispatcher.DispatchAsync(
                    Intent(evt, new NotificationTarget(userId, CustomerId: evt.CustomerId), correlation) with
                    {
                        InApp = new InAppContent(
                            Params: new Dictionary<string, string> { ["title"] = evt.Title },
                            ResourceId: evt.ClientRequestId.ToString(),
                            ResourceContext: new Dictionary<string, string>
                            {
                                ["customerId"] = evt.CustomerId.ToString(),
                            },
                            TemplateKey: "task.client_request_created"
                        ),
                    },
                    ct
                );
            }
        }
    }

    private static NotificationIntent Intent(
        ClientRequestCreatedIntegrationEvent evt,
        NotificationTarget target,
        ICorrelationContext correlation
    ) =>
        new(
            NotificationKinds.TasksClientRequestCreated,
            evt.TenantId,
            target,
            evt.EventId,
            "tasks.client_request_created.v1",
            evt.OccurredOn,
            correlation.CorrelationId
        );
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
            // Quien sigue el pedido (sucesor tras un retiro); los mensajes viejos no lo traen.
            var staffUserId = evt.StaffRecipientUserId ?? evt.RequestedByUserId;

            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.TasksClientRequestFulfilled,
                    evt.TenantId,
                    new NotificationTarget(staffUserId, CustomerId: evt.CustomerId),
                    evt.EventId,
                    "tasks.client_request_fulfilled.v1",
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        Params: new Dictionary<string, string>
                        {
                            ["title"] = evt.Title,
                            ["documentCount"] = evt.DocumentCount.ToString(CultureInfo.InvariantCulture),
                        },
                        ResourceId: evt.ClientRequestId.ToString(),
                        ResourceContext: new Dictionary<string, string> { ["customerId"] = evt.CustomerId.ToString() },
                        TemplateKey: "task.client_request_fulfilled"
                    ),
                    Push: new PushContent()
                ),
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
    private const string DocumentRejectedMessage = "task.document_rejected.client_message";

    public static async Task Handle(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository customers,
        ICustomerContactPolicyRepository contactPolicies,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        IRecipientLocale locales,
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
                dispatcher,
                portal,
                hostResolver,
                locales,
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

        await dispatcher.DispatchAsync(
            Intent(evt, new NotificationTarget(userId, CustomerId: evt.CustomerId), correlation) with
            {
                InApp = InApp(
                    evt,
                    new Dictionary<string, string> { ["displayName"] = evt.DisplayName },
                    "tasks.client_request_document_rejected"
                ),
            },
            ct
        );
    }

    private static async Task NotifyPreparerAsync(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        // Quien sigue el pedido (sucesor tras un retiro); los mensajes viejos no lo traen.
        var staffUserId = evt.StaffRecipientUserId ?? evt.RequestedByUserId;
        await dispatcher.DispatchAsync(
            Intent(evt, new NotificationTarget(staffUserId, CustomerId: evt.CustomerId), correlation) with
            {
                InApp = InApp(
                    evt,
                    new Dictionary<string, string> { ["displayName"] = evt.DisplayName, ["reason"] = evt.Reason },
                    "task.client_request_document_rejected"
                ),
            },
            ct
        );
    }

    private static async Task NotifyCustomerAsync(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository customers,
        ICustomerContactPolicyRepository contactPolicies,
        NotificationDispatcher dispatcher,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        IRecipientLocale locales,
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
        // El mensaje del sistema va en el idioma del cliente, el mismo con el que se renderiza el correo.
        var locale = await locales.ForCustomerAsync(evt.TenantId, evt.CustomerId, ct);

        await dispatcher.DispatchAsync(
            Intent(
                evt,
                new NotificationTarget(Email: customer.NormalizedEmail, CustomerId: evt.CustomerId),
                correlation
            ) with
            {
                Email = new EmailContent(
                    TemplateKey,
                    new Dictionary<string, object?>
                    {
                        ["customer_name"] = customer.DisplayName,
                        ["document_name"] = evt.DisplayName,
                        ["request_title"] = evt.DisplayName,
                        // Mensaje fijo del sistema (Tasks lo manda en un solo idioma): sale en el del cliente.
                        ["client_message"] = EmailText.For(DocumentRejectedMessage, locale),
                        ["portal_link"] = TenantEmailLinks.ClientDocuments(tenantHost, portal.Value),
                    },
                    ScribeRenderAudience.Client
                ),
            },
            ct
        );
    }

    private static NotificationIntent Intent(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        NotificationTarget target,
        ICorrelationContext correlation
    ) =>
        new(
            NotificationKinds.TasksClientRequestDocumentRejected,
            evt.TenantId,
            target,
            evt.EventId,
            "tasks.client_request_document_rejected.v1",
            evt.OccurredOn,
            correlation.CorrelationId
        );

    private static InAppContent InApp(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        Dictionary<string, string> parameters,
        string templateKey
    ) =>
        new(
            Params: parameters,
            ResourceId: evt.ClientRequestId.ToString(),
            ResourceContext: new Dictionary<string, string> { ["customerId"] = evt.CustomerId.ToString() },
            TemplateKey: templateKey
        );
}
