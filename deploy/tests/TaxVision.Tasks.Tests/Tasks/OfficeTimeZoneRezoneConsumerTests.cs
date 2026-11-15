using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Tasks.TimeZones;
using TaxVision.Tasks.ClientForms.Application.Requests.TimeZones;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// P9: al cambiar la zona de la oficina, Tasks recalcula solo los vencimientos abiertos de fecha civil en la zona
/// anterior y guarda una vez. Sin nada que recalcular no guarda.
/// </summary>
public sealed class OfficeTimeZoneRezoneConsumerTests
{
    private const string NewYork = "America/New_York";
    private const string Chicago = "America/Chicago";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 11, 20);
    private static readonly DateTime Now = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Open_civil_dues_of_the_previous_zone_move_to_the_new_one()
    {
        var task = NewTask(DueDate.EndOfCivilDay(Day, ResolvedTimeZone.Resolve(NewYork), false).Value);
        var store = new Store { Tasks = [task] };
        var unitOfWork = new CountingUnitOfWork();

        await OfficeTimeZoneRezoneConsumer.Handle(Changed(), store, unitOfWork, CancellationToken.None);

        Assert.Equal(LocalDay.EndUtc(Day, ResolvedTimeZone.Resolve(Chicago)), task.Due!.DueAtUtc);
        Assert.Equal((Tenant, NewYork), store.AskedFor);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Nothing_to_move_saves_nothing()
    {
        var unitOfWork = new CountingUnitOfWork();

        await OfficeTimeZoneRezoneConsumer.Handle(Changed(), new Store(), unitOfWork, CancellationToken.None);
        await FormRequestRezoneConsumer.Handle(Changed(), new FormStore(), unitOfWork, CancellationToken.None);

        Assert.Equal(0, unitOfWork.Saves);
    }

    private static TenantTimeZoneChangedIntegrationEvent Changed() =>
        new()
        {
            TenantId = Tenant,
            ChangedTenantId = Tenant,
            PreviousTimeZoneId = NewYork,
            NewTimeZoneId = Chicago,
            ChangedAtUtc = Now,
            TenantVersion = 2,
            CorrelationId = "c",
        };

    private static TaskItem NewTask(DueDate due) =>
        TaskItem
            .Create(
                Tenant,
                Guid.NewGuid(),
                TaskTitle.Create("Preparar 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                due,
                null,
                Guid.NewGuid(),
                Now
            )
            .Value;

    private sealed class Store : ICivilDueRezoneStore
    {
        public IReadOnlyList<TaskItem> Tasks { get; init; } = [];
        public (Guid, string)? AskedFor { get; private set; }

        public Task<IReadOnlyList<TaskItem>> OpenTasksWithCivilDuesAsync(
            Guid tenantId,
            string previousTimeZoneId,
            CancellationToken ct
        )
        {
            AskedFor = (tenantId, previousTimeZoneId);
            return Task.FromResult(Tasks);
        }

        public Task<IReadOnlyList<ClientRequest>> OpenClientRequestsWithDueAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClientRequest>>([]);

        public Task<IReadOnlyList<TaskSeries>> ActiveSeriesInZoneAsync(
            Guid tenantId,
            string previousTimeZoneId,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<TaskSeries>>([]);
    }

    private sealed class FormStore : IFormRequestRezoneStore
    {
        public Task<IReadOnlyList<FormRequest>> OpenWithDueAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FormRequest>>([]);
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(0);
        }
    }
}
