using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class DeliveryOutcomePrecedenceTests
{
    [Theory]
    [InlineData(null, DeliveryOutcome.Delivered, true)]
    [InlineData(DeliveryOutcome.Accepted, DeliveryOutcome.Deferred, true)]
    [InlineData(DeliveryOutcome.Deferred, DeliveryOutcome.Delivered, true)]
    // No retrocede: un aviso tardío no deshace lo que ya pasó.
    [InlineData(DeliveryOutcome.Delivered, DeliveryOutcome.Deferred, false)]
    [InlineData(DeliveryOutcome.Delivered, DeliveryOutcome.Accepted, false)]
    [InlineData(DeliveryOutcome.Deferred, DeliveryOutcome.Accepted, false)]
    [InlineData(DeliveryOutcome.Delivered, DeliveryOutcome.Delivered, false)]
    // La queja gana aunque ya estuviera entregado.
    [InlineData(DeliveryOutcome.Delivered, DeliveryOutcome.Complained, true)]
    [InlineData(DeliveryOutcome.HardBounced, DeliveryOutcome.Complained, true)]
    // Un rebote es final: nada lo vuelve a "entregado".
    [InlineData(DeliveryOutcome.HardBounced, DeliveryOutcome.Delivered, false)]
    [InlineData(DeliveryOutcome.HardBounced, DeliveryOutcome.SoftBounced, false)]
    [InlineData(DeliveryOutcome.SoftBounced, DeliveryOutcome.HardBounced, false)]
    [InlineData(DeliveryOutcome.Rejected, DeliveryOutcome.Delivered, false)]
    [InlineData(DeliveryOutcome.Complained, DeliveryOutcome.Complained, false)]
    // Rebote tardío: el servidor aceptó y después devolvió.
    [InlineData(DeliveryOutcome.Delivered, DeliveryOutcome.HardBounced, true)]
    [InlineData(DeliveryOutcome.Deferred, DeliveryOutcome.SoftBounced, true)]
    public void CanAdvance_follows_the_precedence(DeliveryOutcome? current, DeliveryOutcome next, bool expected)
    {
        Assert.Equal(expected, DeliveryOutcomePrecedence.CanAdvance(current, next));
    }
}
