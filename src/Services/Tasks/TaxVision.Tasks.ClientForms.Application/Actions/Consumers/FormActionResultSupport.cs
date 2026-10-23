using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

internal static class FormActionResultSupport
{
    internal static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;

    // Sin tenant ambiente: el lookup lleva el tenant del evento. Una request ajena o inexistente se ignora.
    internal static async Task<FormRequest?> LoadAsync(
        Guid tenantId,
        Guid requestId,
        IFormRequestRepository requests,
        ILogger logger,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(tenantId, requestId, ct);
        if (found.IsFailure)
            return null;
        if (found.Value.TenantId == tenantId)
            return found.Value;
        logger.LogWarning(
            "Security: action result for form request {FormRequestId} arrived for another tenant {TenantId}.",
            requestId,
            tenantId
        );
        return null;
    }
}
