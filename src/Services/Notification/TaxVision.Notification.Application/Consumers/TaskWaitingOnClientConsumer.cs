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
/// <c>task.waiting_on_client.v1</c> — la firma le pidió algo al cliente y hay que avisarle.
///
/// <para>
/// <b>El destinatario es el cliente.</b> Se le avisa por DOS canales: email (via
/// <c>CustomerEmailDirectoryEntry</c>) e in-app en el portal (via <see cref="CustomerPortal"/>). Si
/// no tiene email se queda solo con in-app; si no tiene portal se queda solo con email; si no
/// tiene ninguno se omite en silencio — el preparador ve el estado igual en
/// <c>GET /tasks/waiting-on-client</c>, perder el aviso no pierde el trabajo.
/// </para>
/// </summary>
public static class TaskWaitingOnClientConsumer
{
    private const string TemplateKey = "task.waiting_on_client.v1";

    public static async Task Handle(
        TaskWaitingOnClientIntegrationEvent evt,
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
            var emailAllowed = await CustomerContactPolicyFilter.AllowsAsync(
                evt.TenantId,
                evt.CustomerId,
                CustomerContactChannel.Email,
                contactPolicies,
                ct
            );
            if (emailAllowed && customer is { IsActive: true } && customer.NormalizedEmail.Length > 0)
            {
                // El cliente entra a SU oficina: link al portal bajo el subdominio del tenant.
                var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);

                var render = (
                    await scribeClient.RenderAsync(
                        TemplateKey,
                        evt.TenantId,
                        new Dictionary<string, object?>
                        {
                            ["customer_name"] = customer.DisplayName,
                            ["task_title"] = evt.Title,
                            ["expected_items"] = evt.ExpectedItems,
                            ["client_due_at_utc"] = evt.ClientDueAtUtc,
                            ["tax_year"] = evt.TaxYear,
                            ["portal_link"] = TenantEmailLinks.ClientBase(tenantHost, portal.Value),
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
                var title = $"Tu oficina espera algo: {evt.Title}";
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    $"user:{userId:N}",
                    title,
                    NotificationCategory.DocumentsAndSignatures,
                    "tasks.waiting_on_client",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.TasksWaitingOnClient,
                        userId,
                        evt.EventId,
                        "tasks.waiting_on_client.v1",
                        evt.OccurredOn,
                        title,
                        FallbackBody: evt.ExpectedItems,
                        ResourceId: evt.TaskId.ToString(),
                        ResourceContext: new Dictionary<string, string> { ["customerId"] = evt.CustomerId.ToString() },
                        Params: new Dictionary<string, string> { ["title"] = evt.Title }
                    ),
                    ct: ct
                );
            }
        }
    }
}
