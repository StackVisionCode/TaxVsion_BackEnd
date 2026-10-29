namespace TaxVision.DataCollection.Engine.Serialization;

// Mapeo y acciones de una definición, en la misma forma canónica que el documento completo.
public sealed record CanonicalRuleSets(string MappingSetJson, string ActionSetJson)
{
    public bool IsEmpty => MappingSetJson == "[]" && ActionSetJson == "[]";
}
