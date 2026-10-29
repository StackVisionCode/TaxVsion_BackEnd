using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

// Versión fijada de la request con su definición y sus reglas ya leídas.
public sealed record FormActionContext(FormTemplateVersion Version, FormDefinition Definition, FormRuleSets Rules);
