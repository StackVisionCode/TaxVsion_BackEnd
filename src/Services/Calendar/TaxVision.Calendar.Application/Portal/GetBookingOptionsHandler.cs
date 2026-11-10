using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Booking.Abstractions;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Application.Portal.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Portal;

/// <summary>
/// Qué puede reservar el cliente: los tipos que ofrece la oficina y sus preparadores asignados (D-CAL-C).
/// Si no se ofrece, dice por qué (NotEnabled o NoPreparer) para que el portal muestre el enlace externo.
/// </summary>
public static class GetBookingOptionsHandler
{
    public static async Task<Result<BookingOptionsResponse>> Handle(
        GetBookingOptionsQuery query,
        IPortalCalendarStore store,
        IAppointmentTypeRepository types,
        IBookingPolicyRepository policies,
        IAvailabilityRepository availability,
        IOfficeScheduleRepository offices,
        IStaffContactClient contacts,
        CancellationToken ct
    )
    {
        var policy = await policies.GetAsync(query.TenantId, ct);
        if (policy is not { Enabled: true })
            return Result.Success(Off("NotEnabled"));

        var offered = new List<BookableTypeResponse>();
        foreach (var typeId in policy.BookableTypeIds)
        {
            var type = await types.GetByIdAsync(query.TenantId, typeId, ct);
            if (type.IsSuccess && type.Value.IsActive)
                offered.Add(
                    new BookableTypeResponse(
                        type.Value.Id,
                        type.Value.Name,
                        (int)type.Value.DefaultDuration.TotalMinutes,
                        type.Value.IsVirtual,
                        type.Value.ColorHex
                    )
                );
        }

        if (offered.Count == 0)
            return Result.Success(Off("NotEnabled"));

        var preparers = new List<BookablePreparerResponse>();
        foreach (var userId in await store.ListAssignedStaffAsync(query.TenantId, query.CustomerId, ct))
        {
            var contact = await contacts.FindAsync(query.TenantId, userId, ct);
            if (contact is { IsActive: false })
                continue;
            preparers.Add(
                new BookablePreparerResponse(
                    userId,
                    string.IsNullOrWhiteSpace(contact?.DisplayName) ? "Your preparer" : contact.DisplayName
                )
            );
        }

        if (preparers.Count == 0)
            return Result.Success(Off("NoPreparer"));

        var zone = await BookingSlotFinder.TimeZoneAsync(
            query.TenantId,
            preparers[0].UserId,
            availability,
            offices,
            ct
        );
        return Result.Success(
            new BookingOptionsResponse(
                true,
                null,
                offered,
                preparers,
                zone,
                policy.CustomerChangeCutoffMinutes,
                policy.RequiresApproval
            )
        );
    }

    private static BookingOptionsResponse Off(string reason) => new(false, reason, [], [], null, 0);
}
