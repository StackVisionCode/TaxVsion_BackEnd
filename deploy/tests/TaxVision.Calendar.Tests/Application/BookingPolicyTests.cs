using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Booking;
using TaxVision.Calendar.Application.Booking.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.Types;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

public sealed class BookingPolicyTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private static AppointmentType ActiveType() =>
        AppointmentType.Create(Tenant, "Return review", TimeSpan.FromMinutes(60), "#3366FF", DateTime.UtcNow).Value;

    private static UpdateBookingPolicyCommand Command(bool enabled, params Guid[] types) =>
        new(
            Tenant,
            Admin,
            enabled,
            types,
            MinNoticeMinutes: 1440,
            MaxDaysAhead: 60,
            BufferMinutes: 10,
            SlotStepMinutes: 15,
            CustomerChangeCutoffMinutes: 1440
        );

    [Fact]
    public async Task Without_a_saved_policy_booking_is_off_with_the_defaults()
    {
        var result = await GetBookingPolicyHandler.Handle(
            new GetBookingPolicyQuery(Tenant),
            new Policies(),
            CancellationToken.None
        );

        Assert.False(result.Value.Enabled);
        Assert.Empty(result.Value.BookableTypeIds);
        Assert.Equal(1440, result.Value.MinNoticeMinutes);
        Assert.Equal(60, result.Value.MaxDaysAhead);
        Assert.Equal(15, result.Value.SlotStepMinutes);
    }

    [Fact]
    public async Task Turning_booking_on_saves_the_policy_with_its_bookable_types()
    {
        var type = ActiveType();
        var policies = new Policies();
        var unitOfWork = new CountingUnitOfWork();

        var result = await UpdateBookingPolicyHandler.Handle(
            Command(true, type.Id),
            policies,
            new Types(type),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.Value.Enabled);
        Assert.Equal([type.Id], result.Value.BookableTypeIds);
        Assert.Equal(10, result.Value.BufferMinutes);
        Assert.NotNull(policies.Saved);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Booking_cannot_be_on_without_a_bookable_type()
    {
        var result = await UpdateBookingPolicyHandler.Handle(
            Command(true),
            new Policies(),
            new Types(),
            new CountingUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(BookingPolicyErrors.NoBookableTypes, result.Error);
    }

    [Fact]
    public async Task An_inactive_or_unknown_type_cannot_be_bookable()
    {
        var inactive = ActiveType();
        inactive.Deactivate();

        var result = await UpdateBookingPolicyHandler.Handle(
            Command(true, inactive.Id, Guid.NewGuid()),
            new Policies(),
            new Types(inactive),
            new CountingUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(BookingPolicyErrors.TypeNotBookable, result.Error);
    }

    [Theory]
    [InlineData(-1, 60, 0, 15)]
    [InlineData(1440, 0, 0, 15)]
    [InlineData(1440, 60, 500, 15)]
    [InlineData(1440, 60, 0, 7)]
    public async Task Out_of_range_values_are_rejected(int notice, int daysAhead, int buffer, int step)
    {
        var result = await UpdateBookingPolicyHandler.Handle(
            new UpdateBookingPolicyCommand(Tenant, Admin, false, [], notice, daysAhead, buffer, step, 1440),
            new Policies(),
            new Types(),
            new CountingUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(BookingPolicyErrors.OutOfRange, result.Error);
    }

    private sealed class Policies : IBookingPolicyRepository
    {
        public BookingPolicy? Saved { get; private set; }

        public Task<BookingPolicy?> GetAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(Saved);

        public void Add(BookingPolicy policy) => Saved = policy;
    }

    private sealed class Types(params AppointmentType[] types) : IAppointmentTypeRepository
    {
        public Task<Result<AppointmentType>> GetByIdAsync(Guid tenantId, Guid typeId, CancellationToken ct = default) =>
            Task.FromResult(
                types.FirstOrDefault(t => t.Id == typeId) is { } found
                    ? Result.Success(found)
                    : Result.Failure<AppointmentType>(TypeErrors.NotFound)
            );

        public Task<IReadOnlyList<AppointmentType>> ListAsync(
            Guid tenantId,
            bool onlyActive,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<AppointmentType>>(types.Where(t => !onlyActive || t.IsActive).ToList());

        public Task<bool> AnyAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(types.Length > 0);

        public void Add(AppointmentType type) { }
    }
}
