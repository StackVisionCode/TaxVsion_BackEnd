using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Library;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class LibrarySeedValidationTests
{
    private static readonly IFormEngine Engine = new FormEngine(new DataCollectionLimits());

    private static readonly string[] ExpectedKeys =
    [
        "individual-tax-intake",
        "business-intake",
        "document-checklist",
        "client-onboarding",
        "what-changed-this-year",
    ];

    public static TheoryData<string, string> Seeds()
    {
        var data = new TheoryData<string, string>();
        foreach (var seed in LibrarySeedCatalog.All())
            data.Add(seed.LibraryKey, seed.DefinitionJson);
        return data;
    }

    [Fact]
    public void Catalog_exposes_the_five_library_seeds()
    {
        var keys = LibrarySeedCatalog.All().Select(seed => seed.LibraryKey).ToArray();
        Assert.Equal(ExpectedKeys.Length, keys.Length);
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.All(ExpectedKeys, key => Assert.Contains(key, keys));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Seed_parses_and_passes_validate_for_publish(string key, string json)
    {
        var parsed = Engine.ParseDefinition(json);
        Assert.True(
            parsed.IsSuccess,
            $"{key} did not parse: {(parsed.IsFailure ? parsed.Error.Message : string.Empty)}"
        );

        var report = Engine.ValidateForPublish(parsed.Value);
        Assert.True(
            report.IsValid,
            $"{key} failed ValidateForPublish: "
                + string.Join("; ", report.Issues.Select(issue => $"{issue.Code}@{issue.Path}"))
        );
    }
}
