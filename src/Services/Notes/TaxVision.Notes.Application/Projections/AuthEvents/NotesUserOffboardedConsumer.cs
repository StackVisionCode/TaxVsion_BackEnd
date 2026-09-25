using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.AuthEvents;

/// <summary>
/// Registra al empleado retirado en <see cref="OffboardedStaffProjection"/> para habilitar el
/// manage-override de edición (un staff con <c>notes.view_all</c> puede gestionar el contenido de las
/// notas huérfanas del que se fue). Idempotente: offboard es terminal, si ya está registrado no hace
/// nada. NO toca la autoría (<c>CreatedByUserId</c>) de ninguna nota. Auto-wire por el fanout
/// <c>taxvision-events</c> (la cola de Notes ya lo bindea).
/// </summary>
public static class NotesUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent evt,
        IOffboardedStaffRepository offboardedStaff,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var existing = await offboardedStaff.GetAsync(evt.TenantId, evt.UserId, ct);
            if (existing is not null)
                return;

            await offboardedStaff.AddAsync(
                OffboardedStaffProjection.Create(evt.TenantId, evt.UserId, evt.RemovedAtUtc),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
