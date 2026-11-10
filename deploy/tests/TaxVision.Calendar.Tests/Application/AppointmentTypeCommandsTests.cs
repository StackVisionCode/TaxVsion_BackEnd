using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Application.Types.Commands;
using TaxVision.Calendar.Domain.Types;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

public sealed class AppointmentTypeCommandsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static AppointmentType NewType() =>
        AppointmentType.Create(Tenant, "Return review", TimeSpan.FromMinutes(60), "#3366FF", DateTime.UtcNow).Value;

    [Fact]
    public async Task Update_changes_the_type_and_saves()
    {
        var type = NewType();
        var unitOfWork = new CountingUnitOfWork();

        var result = await UpdateAppointmentTypeHandler.Handle(
            new UpdateAppointmentTypeCommand(
                Tenant,
                type.Id,
                "Document drop-off",
                TimeSpan.FromMinutes(15),
                "#22aa66",
                false,
                true,
                6
            ),
            new TypesById(type),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Document drop-off", result.Value.Name);
        Assert.Equal(TimeSpan.FromMinutes(15), result.Value.DefaultDuration);
        Assert.Equal("#22AA66", result.Value.ColorHex);
        Assert.True(result.Value.BlocksOnConflict);
        Assert.Equal(6, result.Value.DailyCap);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Update_with_invalid_values_does_not_save()
    {
        var type = NewType();
        var unitOfWork = new CountingUnitOfWork();

        var result = await UpdateAppointmentTypeHandler.Handle(
            new UpdateAppointmentTypeCommand(
                Tenant,
                type.Id,
                " ",
                TimeSpan.FromMinutes(60),
                "#3366FF",
                false,
                false,
                null
            ),
            new TypesById(type),
            unitOfWork,
            CancellationToken.None
        );

        Assert.Equal(TypeErrors.NameEmpty, result.Error);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Deactivate_and_reactivate_toggle_the_type()
    {
        var type = NewType();
        var repository = new TypesById(type);

        var off = await SetAppointmentTypeActiveHandler.Handle(
            new SetAppointmentTypeActiveCommand(Tenant, type.Id, false),
            repository,
            new CountingUnitOfWork(),
            CancellationToken.None
        );
        Assert.False(off.Value.IsActive);

        var on = await SetAppointmentTypeActiveHandler.Handle(
            new SetAppointmentTypeActiveCommand(Tenant, type.Id, true),
            repository,
            new CountingUnitOfWork(),
            CancellationToken.None
        );
        Assert.True(on.Value.IsActive);
    }

    [Fact]
    public async Task Unknown_type_is_not_found()
    {
        var result = await SetAppointmentTypeActiveHandler.Handle(
            new SetAppointmentTypeActiveCommand(Tenant, Guid.NewGuid(), false),
            new TypesById(NewType()),
            new CountingUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(TypeErrors.NotFound, result.Error);
    }

    private sealed class TypesById(AppointmentType type) : IAppointmentTypeRepository
    {
        public Task<Result<AppointmentType>> GetByIdAsync(Guid tenantId, Guid typeId, CancellationToken ct = default) =>
            Task.FromResult(
                tenantId == type.TenantId && typeId == type.Id
                    ? Result.Success(type)
                    : Result.Failure<AppointmentType>(TypeErrors.NotFound)
            );

        public Task<IReadOnlyList<AppointmentType>> ListAsync(
            Guid tenantId,
            bool onlyActive,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<AppointmentType>>([type]);

        public Task<bool> AnyAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(true);

        public void Add(AppointmentType added) { }
    }
}
