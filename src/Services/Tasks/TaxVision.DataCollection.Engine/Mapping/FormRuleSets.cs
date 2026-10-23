using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Mapping;

// Mapeo y acciones fijados en una versión, leídos de su forma canónica.
public sealed record FormRuleSets(IReadOnlyList<FormMapping> Mappings, IReadOnlyList<FormAction> Actions);
