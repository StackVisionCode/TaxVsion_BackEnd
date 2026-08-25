using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// <c>task.client_request_created.v1</c> — la firma le pidió algo al cliente y hay que decírselo.
///
/// <para>
/// El destinatario es el cliente, así que se resuelve contra el directorio de clientes y sale por
/// correo: no tiene sesión en el panel interno, un in-app no lo vería nunca.
/// </para>
/// </summary>
public static class ClientRequestCreatedConsumer
{
    private const string TemplateKey = "task.client_request_created.v1";

    public static async Task Handle(
        ClientRequestCreatedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository customers,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var customer = await customers.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (customer is null || !customer.IsActive || customer.NormalizedEmail.Length == 0)
                return;

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
                        ["portal_link"] = TenantEmailLinks.ClientBase(tenantHost, portal.Value),
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered(TemplateKey);

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: customer.NormalizedEmail,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: TemplateKey,
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );
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

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                title,
                title,
                NotificationCategory.DocumentsAndSignatures,
                "task.client_request_fulfilled",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.RequestedByUserId,
                ct: ct
            );

            await dispatcher.SendPushAsync(
                evt.TenantId,
                evt.RequestedByUserId,
                title,
                evt.DocumentCount == 1 ? "Subió un documento." : $"Subió {evt.DocumentCount} documentos.",
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
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        NotificationDispatcher dispatcher,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await NotifyPreparerAsync(evt, dispatcher, correlation, ct);
            await NotifyCustomerAsync(evt, customers, gateway, scribeClient, portal, hostResolver, correlation, ct);
        }
    }

    private static async Task NotifyPreparerAsync(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        await dispatcher.RecordInAppAsync(
            evt.TenantId,
            $"Archivo del cliente rechazado: {evt.DisplayName} ({evt.Reason})",
            $"Archivo del cliente rechazado: {evt.DisplayName} ({evt.Reason})",
            NotificationCategory.DocumentsAndSignatures,
            "task.client_request_document_rejected",
            evt.EventId,
            correlation.CorrelationId,
            recipientUserId: evt.RequestedByUserId,
            ct: ct
        );

    private static async Task NotifyCustomerAsync(
        ClientRequestDocumentRejectedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository customers,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
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
                    ["portal_link"] = TenantEmailLinks.ClientBase(tenantHost, portal.Value),
                    ["product_name"] = portal.Value.ProductName,
                },
                ct
            )
        ).EnsureRendered(TemplateKey);

        await gateway.QueueEmailAsync(
            new EmailDispatchRequest(
                TenantId: evt.TenantId,
                To: customer.NormalizedEmail,
                Subject: render.Subject,
                HtmlBody: render.Html,
                TextBody: render.Text ?? string.Empty,
                TemplateKey: TemplateKey,
                RelatedEventId: evt.EventId,
                CorrelationId: correlation.CorrelationId,
                InlineAssets: render.InlineAssets
            ),
            ct
        );
    }
}
