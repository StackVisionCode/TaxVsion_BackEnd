namespace TaxVision.Reminder.Tests.Observability;

/// <summary>
/// <see cref="Infrastructure.Observability.ReminderMetrics"/> usa un <c>Meter</c> de nombre fijo, y
/// un <c>MeterListener</c> suscripto por nombre recibe mediciones de <b>cualquier</b> instancia de
/// ese Meter — incluida la de otra clase de test corriendo en paralelo. Agrupar acá toda clase que
/// escuche ese Meter para que xUnit no las corra concurrentemente entre sí; el resto de la suite
/// sigue en paralelo, porque no lo toca.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ReminderMetricsCollection
{
    public const string Name = "ReminderMetrics (serialized — shared Meter)";
}
