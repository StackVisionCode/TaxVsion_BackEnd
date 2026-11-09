namespace TaxVision.Campaigns.Application.Scheduling;

/// <summary>
/// El directorio de clientes no respondió completo al disparar un agendado. Transitoria: la transacción se
/// revierte, el lease vence y el próximo claim reintenta el mismo slot.
/// </summary>
public sealed class CustomerAudienceUnavailableException(Guid scheduleId, DateTime slotUtc)
    : Exception($"Customer audience unavailable for schedule {scheduleId} slot {slotUtc:o}.");
