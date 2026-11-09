using System.Reflection;
using BuildingBlocks.Domain;
using TaxVision.BuildingBlocks.Testing.Messaging;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>La regla de BB-1 sobre el sobre de Wolverine, contra tipos de muestra.</summary>
public sealed class DomainEventEnvelopeRoundTripTests
{
    [Fact]
    public void PrimitiveEvent_Survives()
    {
        Assert.Empty(DomainEventEnvelopeRoundTrip.FindEventsThatDoNotSurvive([typeof(PrimitiveEvent)]));
    }

    // La igualdad del record daría falso: la lista releída es otra instancia.
    [Fact]
    public void EventWithCollections_Survives()
    {
        Assert.Empty(DomainEventEnvelopeRoundTrip.FindEventsThatDoNotSurvive([typeof(CollectionEvent)]));
    }

    [Fact]
    public void ValueObjectWithPrivateConstructor_IsReported()
    {
        var broken = Assert.Single(
            DomainEventEnvelopeRoundTrip.FindEventsThatDoNotSurvive([typeof(PrivateConstructorEvent)])
        );

        Assert.Contains(nameof(PrivateConstructorEvent), broken);
    }

    // No lanza nada: el valor se pierde en silencio.
    [Fact]
    public void ValueObjectWithPrivateSetter_IsReported()
    {
        var broken = Assert.Single(
            DomainEventEnvelopeRoundTrip.FindEventsThatDoNotSurvive([typeof(PrivateSetterEvent)])
        );

        Assert.Contains("AmountCents", broken);
    }

    // Tras releer tendría la hora del reinicio, no la del hecho.
    [Fact]
    public void GetOnlyPropertyOutsideTheConstructor_IsReported()
    {
        var broken = Assert.Single(
            DomainEventEnvelopeRoundTrip.FindEventsThatDoNotSurvive([typeof(GetOnlyPropertyEvent)])
        );

        Assert.Contains("OccurredAtUtc", broken);
    }

    [Fact]
    public void InterfaceProperty_IsReported()
    {
        var broken = Assert.Single(
            DomainEventEnvelopeRoundTrip.FindEventsThatDoNotSurvive([typeof(InterfacePropertyEvent)])
        );

        Assert.Contains("no se pudo armar", broken);
    }

    [Fact]
    public void AbstractEventsAndOtherTypes_AreIgnored()
    {
        Assert.Empty(
            DomainEventEnvelopeRoundTrip.FindEventsThatDoNotSurvive([
                typeof(AbstractEvent),
                typeof(PrivateConstructorAmount),
            ])
        );
    }

    [Fact]
    public void Sample_FillsEveryProperty()
    {
        var sample = DomainEventEnvelopeRoundTrip.BuildSample(typeof(PrimitiveEvent));

        foreach (var property in typeof(PrimitiveEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = property.GetValue(sample);
            Assert.NotNull(value);
            if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null)
            {
                Assert.NotEqual(Activator.CreateInstance(property.PropertyType), value);
            }
        }
    }

    [Fact]
    public void Sample_GivesDifferentValuesToPropertiesOfTheSameType()
    {
        var sample = (PrimitiveEvent)DomainEventEnvelopeRoundTrip.BuildSample(typeof(PrimitiveEvent));

        Assert.NotEqual(sample.Id, sample.OtherId);
    }

    public enum SampleStatus
    {
        None,
        Done,
    }

    public sealed record PrimitiveEvent(
        Guid Id,
        Guid OtherId,
        string Name,
        int Count,
        long AmountCents,
        decimal Rate,
        bool Flag,
        DateTime AtUtc,
        DateTimeOffset At,
        Guid? OptionalId,
        DateTime? OptionalAtUtc,
        SampleStatus Status
    ) : IDomainEvent;

    public sealed record CollectionEvent(
        IReadOnlyList<Guid> Ids,
        string[] Names,
        IReadOnlyDictionary<string, int> Counts,
        HashSet<string> Tags
    ) : IDomainEvent;

    public sealed record PrivateConstructorEvent(Guid Id, PrivateConstructorAmount Amount) : IDomainEvent;

    public sealed record PrivateSetterEvent(Guid Id, PrivateSetterAmount Amount) : IDomainEvent;

    public sealed record GetOnlyPropertyEvent(Guid Id) : IDomainEvent
    {
        public DateTime OccurredAtUtc { get; } = DateTime.UtcNow;
    }

    public sealed record InterfacePropertyEvent(Guid Id, ISampleContract Contract) : IDomainEvent;

    public abstract record AbstractEvent : IDomainEvent;

    public interface ISampleContract;

    public sealed class PrivateConstructorAmount
    {
        private PrivateConstructorAmount(long cents) => AmountCents = cents;

        public long AmountCents { get; }
    }

    public sealed class PrivateSetterAmount
    {
        public long AmountCents { get; private set; }
    }
}
