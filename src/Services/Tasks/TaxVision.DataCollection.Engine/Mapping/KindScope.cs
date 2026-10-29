namespace TaxVision.DataCollection.Engine.Mapping;

// Valores posibles del discriminador para una acción y cómo leer condiciones contra ellos.
internal sealed record KindScope(IReadOnlySet<string> Kinds, KindConditionAnalyzer Analyzer)
{
    internal static KindScope Empty { get; } =
        new(new HashSet<string>(StringComparer.Ordinal), new KindConditionAnalyzer(null, null));
}
