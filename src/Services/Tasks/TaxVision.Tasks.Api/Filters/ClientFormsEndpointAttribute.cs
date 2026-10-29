namespace TaxVision.Tasks.Api.Filters;

// Marca explícita de los controllers de Client Forms: el filtro de disponibilidad solo mira esta marca.
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ClientFormsEndpointAttribute : Attribute;
