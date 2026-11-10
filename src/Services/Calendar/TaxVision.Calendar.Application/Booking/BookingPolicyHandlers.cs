using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Booking.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Booking;

namespace TaxVision.Calendar.Application.Booking;

public sealed record BookingPolicyResponse(
    bool Enabled,
    IReadOnlyList<Guid> BookableTypeIds,
    int MinNoticeMinutes,
    int MaxDaysAhead,
    int BufferMinutes,
    int SlotStepMinutes,
    int CustomerChangeCutoffMinutes,
    DateTime? UpdatedAtUtc,
    bool RequiresApproval
)
{
    public static BookingPolicyResponse From(BookingPolicy policy) =>
        new(
            policy.Enabled,
            policy.BookableTypeIds,
            policy.MinNoticeMinutes,
            policy.MaxDaysAhead,
            policy.BufferMinutes,
            policy.SlotStepMinutes,
            policy.CustomerChangeCutoffMinutes,
            policy.UpdatedAtUtc == default ? null : policy.UpdatedAtUtc,
            policy.RequiresApproval
        );
}

public sealed record GetBookingPolicyQuery(Guid TenantId);

/// <summary>La política guardada o, si nunca se guardó, la de fábrica (reserva apagada).</summary>
public static class GetBookingPolicyHandler
{
    public static async Task<Result<BookingPolicyResponse>> Handle(
        GetBookingPolicyQuery query,
        IBookingPolicyRepository policies,
        CancellationToken ct
    )
    {
        var policy = await policies.GetAsync(query.TenantId, ct) ?? BookingPolicy.Default(query.TenantId);
        return Result.Success(BookingPolicyResponse.From(policy));
    }
}

public sealed record UpdateBookingPolicyCommand(
    Guid TenantId,
    Guid ActorId,
    bool Enabled,
    IReadOnlyList<Guid> BookableTypeIds,
    int MinNoticeMinutes,
    int MaxDaysAhead,
    int BufferMinutes,
    int SlotStepMinutes,
    int CustomerChangeCutoffMinutes,
    bool RequiresApproval = true
);

/// <summary>Guarda la política completa. Solo se ofrecen tipos que existen y están activos.</summary>
public static class UpdateBookingPolicyHandler
{
    public static async Task<Result<BookingPolicyResponse>> Handle(
        UpdateBookingPolicyCommand command,
        IBookingPolicyRepository policies,
        IAppointmentTypeRepository types,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var active = (await types.ListAsync(command.TenantId, onlyActive: true, ct)).Select(t => t.Id).ToHashSet();
        if (command.BookableTypeIds.Any(id => !active.Contains(id)))
            return Result.Failure<BookingPolicyResponse>(BookingPolicyErrors.TypeNotBookable);

        var existing = await policies.GetAsync(command.TenantId, ct);
        var policy = existing ?? BookingPolicy.Default(command.TenantId);
        var updated = policy.Update(
            command.Enabled,
            command.BookableTypeIds,
            command.MinNoticeMinutes,
            command.MaxDaysAhead,
            command.BufferMinutes,
            command.SlotStepMinutes,
            command.CustomerChangeCutoffMinutes,
            DateTime.UtcNow,
            command.RequiresApproval
        );
        if (updated.IsFailure)
            return Result.Failure<BookingPolicyResponse>(updated.Error);

        if (existing is null)
            policies.Add(policy);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(BookingPolicyResponse.From(policy));
    }
}
