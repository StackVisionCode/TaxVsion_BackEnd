using BuildingBlocks.Common;
using TaxVision.Sms.Application.Messages.Queries;
using TaxVision.Sms.Application.OptOut.Queries;

namespace TaxVision.Sms.Application.Abstractions;

/// <summary>
/// Read model del CRM sobre las tablas ya persistidas (smsMessages/smsMedia/smsOptOuts). Solo lectura;
/// aislado por tenant con filtro explícito (los handlers de query corren en un scope de Wolverine
/// distinto al de la request HTTP, así que no se puede depender del HasQueryFilter ambiental).
/// </summary>
public interface ISmsReadService
{
    // assignedToUserId (P2): si no es null, restringe a los clientes asignados a ese usuario (visibilidad por
    // asignación). null = sin restricción (view_all / flag apagado / uso interno). Opcional para no romper callers.
    Task<PagedResult<SmsMessageSummaryResponse>> SearchMessagesAsync(
        Guid tenantId,
        Guid? customerId,
        SmsMessageStatusFilter status,
        string? term,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? sourceContext,
        int page,
        int size,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    );

    Task<SmsMessageDetailResponse?> GetMessageByIdAsync(
        Guid tenantId,
        Guid messageId,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    );

    Task<SmsStatsResponse> GetStatsAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        string? sourceContext,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    );

    Task<PagedResult<SmsOptOutSummaryResponse>> SearchOptOutsAsync(
        Guid tenantId,
        SmsOptOutStatusFilter status,
        string? term,
        int page,
        int size,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    );
}
