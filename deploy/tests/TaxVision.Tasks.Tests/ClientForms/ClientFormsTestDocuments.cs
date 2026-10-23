using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests;

// Los value objects solo se construyen por su factory; los tests pasan por el mismo camino.
internal static class ClientFormsTestDocuments
{
    internal static FormDefinitionDocument Definition(string json) =>
        FormDefinitionDocument.Create(json, FormDefinitionDocument.MaxBytes).Value;

    internal static ContentHash Hash(string value) => ContentHash.Create(value).Value;
}
