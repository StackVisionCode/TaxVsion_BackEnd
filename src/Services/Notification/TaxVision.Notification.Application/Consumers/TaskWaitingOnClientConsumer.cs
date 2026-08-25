using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// <c>task.waiting_on_client.v1</c> — la firma le pidió algo al cliente y hay que avisarle.
///
/// <para>
/// <b>El destinatario es el cliente, no personal de la firma.</b> Por eso resuelve contra
/// <c>CustomerEmailDirectoryEntry</c> y no contra el directorio de usuarios, y por eso no hay in-app
/// ni push: el cliente no tiene sesión en el panel interno. El aviso al preparador de que el cliente
/// todavía no respondió lo maneja Reminder por su lado, con <c>reminder.requested.v1</c>.
/// </para>
///
/// <para>
/// Sin dirección resoluble se omite en silencio y se deja el rastro en el log: el preparador ve el
/// estado igual en <c>GET /tasks/waiting-on-client</c>, así que perder el correo no pierde el
/// trabajo. Fingir que salió sería peor.
/// </para>
/// </summary>
public static class TaskWaitingOnClientConsumer
{
    private const string TemplateKey = "task.waiting_on_client.v1";

    public static async Task Handle(
        TaskWaitingOnClientIntegrationEvent evt,
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
