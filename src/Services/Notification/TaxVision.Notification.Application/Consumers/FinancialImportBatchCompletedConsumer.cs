using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.FinancialInsightsIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Lote de estados de cuenta terminado. Staff: aviso a quien lo subió. Portal: acuse al cliente y aviso al
/// staff asignado al cliente (si nadie asignado pasa la validación, al owner). Todo pasa por el gate del
/// módulo <c>financial_insights</c> del resolver.
/// </summary>
public static class FinancialImportBatchCompletedConsumer
{
    private const string SourceEventType = "financial.import_batch.completed.v1";

    private static readonly RecipientValidation StaffValidation = new(
        RequiredCapability: FinancialInsightsPermissions.View,
        RequiredModule: PermissionModuleMap.ModuleFor(FinancialInsightsPermissions.View)
    );

    // El cliente no tiene RBAC de empleado: solo módulo.
    private static readonly RecipientValidation PortalValidation = new(
        RequiredModule: PermissionModuleMap.ModuleFor(FinancialInsightsPermissions.PortalImport)
    );

    public static async Task Handle(
        FinancialImportBatchCompletedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICustomerAssignmentAudienceRepository assignments,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (string.Equals(evt.Source, FinancialImportBatchSources.Portal, StringComparison.OrdinalIgnoreCase))
            {
                await NotifyPortalClientAsync(evt, dispatcher, resolver, correlation, ct);
                await NotifyOfficeAsync(evt, dispatcher, assignments, resolver, correlation, ct);
                return;
            }

            await NotifyCreatorAsync(evt, dispatcher, resolver, correlation, ct);
        }
    }

    private static async Task NotifyCreatorAsync(
        FinancialImportBatchCompletedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (evt.CreatedByUserId == Guid.Empty)
            return;

        var recipients = await resolver.ResolveAsync(
            new Participants(evt.TenantId, [evt.CreatedByUserId]),
            StaffValidation,
            ct
        );
        if (recipients.IsFailure)
            return;

        var title = NeedsAttention(evt) ? "Bank statements need attention" : "Bank statements processed";
        foreach (var userId in recipients.Value)
            await RecordAsync(
                evt,
                dispatcher,
                correlation,
                userId,
                NotificationKinds.FinancialImportBatchCompleted,
                title,
                StaffBody(evt),
                ct
            );
    }

    private static async Task NotifyPortalClientAsync(
        FinancialImportBatchCompletedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var portal = await resolver.ResolveAsync(
            new CustomerPortal(evt.TenantId, evt.CustomerId),
            PortalValidation,
            ct
        );
        // Solo si quien lo subió sigue siendo la cuenta de portal activa del cliente.
        if (portal.IsFailure || !portal.Value.Contains(evt.CreatedByUserId))
            return;

        // NeedsReview lo resuelve la oficina; al cliente solo le importa lo que no se pudo leer.
        var (title, body) =
            evt.FailedDocuments + evt.RejectedDocuments > 0
                ? (
                    "Some of your bank statements need attention",
                    "We couldn't read some of your files. Your tax office will contact you if anything else is needed."
                )
                : (
                    "Your bank statements were received and analyzed",
                    "Your tax office has been notified and will review them."
                );

        await RecordAsync(
            evt,
            dispatcher,
            correlation,
            evt.CreatedByUserId,
            NotificationKinds.FinancialPortalImportBatchCompleted,
            title,
            body,
            ct
        );
    }

    private static async Task NotifyOfficeAsync(
        FinancialImportBatchCompletedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICustomerAssignmentAudienceRepository assignments,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        IReadOnlyList<Guid> recipients = [];
        var assigned = await assignments.GetAssignedUserIdsAsync(evt.TenantId, evt.CustomerId, ct);
        if (assigned.Count > 0)
        {
            var resolved = await resolver.ResolveAsync(
                new Participants(evt.TenantId, assigned.ToArray()),
                StaffValidation,
                ct
            );
            recipients = resolved.IsSuccess ? resolved.Value : [];
        }

        // Sin asignados válidos el lote no puede quedar sin dueño: va al owner de la oficina.
        if (recipients.Count == 0)
        {
            var owner = await resolver.ResolveAsync(new TenantOwnerAudience(evt.TenantId), StaffValidation, ct);
            recipients = owner.IsSuccess ? owner.Value : [];
        }

        var title = NeedsAttention(evt)
            ? "Client bank statements need attention"
            : "Client bank statements ready to review";
        foreach (var userId in recipients)
            await RecordAsync(
                evt,
                dispatcher,
                correlation,
                userId,
                NotificationKinds.FinancialPortalImportBatchSubmitted,
                title,
                StaffBody(evt),
                ct
            );
    }

    private static Task RecordAsync(
        FinancialImportBatchCompletedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        Guid userId,
        string kind,
        string title,
        string body,
        CancellationToken ct
    ) =>
        dispatcher.RecordInAppAsync(
            evt.TenantId,
            NotificationRecipient.ForUser(userId),
            title,
            NotificationCategory.DocumentsAndSignatures,
            kind,
            evt.EventId,
            correlation.CorrelationId,
            recipientUserId: userId,
            inApp: new InAppNotificationRequest(
                kind,
                userId,
                evt.EventId,
                SourceEventType,
                evt.CompletedAtUtc,
                title,
                FallbackBody: body,
                ResourceId: evt.BatchId.ToString(),
                ResourceContext: new Dictionary<string, string>
                {
                    ["batchId"] = evt.BatchId.ToString(),
                    ["customerId"] = evt.CustomerId.ToString(),
                },
                Params: new Dictionary<string, string>
                {
                    ["completedDocuments"] = evt.CompletedDocuments.ToString(),
                    ["needsReviewDocuments"] = evt.NeedsReviewDocuments.ToString(),
                    ["failedDocuments"] = evt.FailedDocuments.ToString(),
                    ["rejectedDocuments"] = evt.RejectedDocuments.ToString(),
                    ["totalDocuments"] = TotalDocuments(evt).ToString(),
                }
            ),
            ct: ct
        );

    private static bool NeedsAttention(FinancialImportBatchCompletedIntegrationEvent evt) =>
        evt.NeedsReviewDocuments + evt.FailedDocuments + evt.RejectedDocuments > 0;

    private static int TotalDocuments(FinancialImportBatchCompletedIntegrationEvent evt) =>
        evt.CompletedDocuments + evt.NeedsReviewDocuments + evt.FailedDocuments + evt.RejectedDocuments;

    private static string StaffBody(FinancialImportBatchCompletedIntegrationEvent evt)
    {
        if (!NeedsAttention(evt))
            return evt.CompletedDocuments == 1
                ? "1 statement is ready to review."
                : $"{evt.CompletedDocuments} statements are ready to review.";

        var parts = new List<string>();
        if (evt.CompletedDocuments > 0)
            parts.Add($"{evt.CompletedDocuments} ready");
        if (evt.NeedsReviewDocuments > 0)
            parts.Add($"{evt.NeedsReviewDocuments} {(evt.NeedsReviewDocuments == 1 ? "needs" : "need")} review");
        if (evt.FailedDocuments > 0)
            parts.Add($"{evt.FailedDocuments} failed");
        if (evt.RejectedDocuments > 0)
            parts.Add($"{evt.RejectedDocuments} rejected");
        return string.Join(", ", parts) + ".";
    }
}
