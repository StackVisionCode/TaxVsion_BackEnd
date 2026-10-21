namespace TaxVision.Tasks.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipClientFormsAccessAttribute : Attribute;
