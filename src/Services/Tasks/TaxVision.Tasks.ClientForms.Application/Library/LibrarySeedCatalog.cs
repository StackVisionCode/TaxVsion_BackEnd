using System.Reflection;

namespace TaxVision.Tasks.ClientForms.Application.Library;

public sealed record LibrarySeed(string LibraryKey, string Description, string DefinitionJson);

// Las 5 plantillas de la biblioteca viajan como JSON EmbeddedResource; el LibrarySeeder las crea una vez.
public static class LibrarySeedCatalog
{
    private static readonly (string Key, string Description)[] Definitions =
    [
        (
            "individual-tax-intake",
            "Collect personal details, filing status, dependents and income sources for an individual return."
        ),
        (
            "business-intake",
            "Collect entity details, ownership, accounting method and documents for a business return."
        ),
        (
            "document-checklist",
            "Ask clients to upload the documents you need, with an \"I don't have this\" option for each."
        ),
        ("client-onboarding", "Welcome a new client and collect basic contact details and preferences."),
        ("what-changed-this-year", "A short questionnaire about the life changes that affect this year's return."),
    ];

    public static IReadOnlyList<LibrarySeed> All()
    {
        var assembly = typeof(LibrarySeedCatalog).Assembly;
        return
        [
            .. Definitions.Select(definition => new LibrarySeed(
                definition.Key,
                definition.Description,
                ReadResource(assembly, definition.Key)
            )),
        ];
    }

    private static string ReadResource(Assembly assembly, string key)
    {
        // MSBuild puede normalizar guiones del nombre lógico; se admite guion y guion bajo.
        var candidates = new[] { $"Seed.{key}.json", $"Seed.{key.Replace('-', '_')}.json" };
        var name = assembly
            .GetManifestResourceNames()
            .Single(resource => candidates.Any(candidate => resource.EndsWith(candidate, StringComparison.Ordinal)));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
