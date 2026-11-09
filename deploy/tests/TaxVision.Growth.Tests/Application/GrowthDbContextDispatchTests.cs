using BuildingBlocks.Domain;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Growth.Tests.Domain;
using Wolverine;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// Ningún evento de dominio sale por el IMessageBus al guardar: ese camino iba en memoria, fuera de la transacción.
/// Los saca Wolverine del agregado al confirmar. El contexto se arma con DI para que la prueba valga con el bus en
/// el constructor (rojo) y sin él (verde).
/// </summary>
public sealed class GrowthDbContextDispatchTests
{
    [Fact]
    public async Task SaveChanges_LeavesDomainEventsOnTheAggregateAndNothingOnTheBus()
    {
        var bus = System.Reflection.DispatchProxy.Create<IMessageBus, RecordingBus>();
        await using var provider = new ServiceCollection()
            .AddSingleton(
                new DbContextOptionsBuilder<GrowthDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options
            )
            .AddSingleton<ITenantContext>(new NoTenant())
            .AddSingleton(bus)
            .BuildServiceProvider();
        await using var db = ActivatorUtilities.CreateInstance<GrowthDbContext>(provider);

        var definition = GrowthTestData.CreateActivePercentageCode();
        var quote = GrowthTestData.CreateQuote(definition, GrowthTestData.RefereeTenantId);
        var reservation = GrowthTestData.CreateReservation(definition, quote);
        db.CodeReservations.Add(reservation);
        await db.SaveChangesAsync();

        Assert.Empty(((RecordingBus)(object)bus).Published.OfType<IDomainEvent>());
        Assert.Single(reservation.DomainEvents);
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    public class RecordingBus : System.Reflection.DispatchProxy
    {
        public List<object> Published { get; } = [];

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IMessageBus.PublishAsync))
                throw new NotSupportedException($"El doble de bus no implementa {targetMethod?.Name}.");
            Published.Add(args![0]!);
            return ValueTask.CompletedTask;
        }
    }
}
