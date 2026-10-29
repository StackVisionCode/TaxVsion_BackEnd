using System.Text.Json;
using System.Text.Json.Serialization;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.DataCollection.Engine.Progress;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.DataCollection.Engine.Visibility;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class FixtureContractTests
{
    private const int FixtureSetVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly JsonSerializerOptions CatalogOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static IEnumerable<object[]> Cases()
    {
        var root = FixtureRoot();
        var manifest =
            JsonSerializer.Deserialize<FixtureManifest>(
                File.ReadAllText(Path.Combine(root, "manifest.json")),
                JsonOptions
            ) ?? throw new InvalidOperationException("Fixture manifest is invalid.");

        Assert.Equal(FixtureSetVersion, manifest.FixtureSetVersion);
        foreach (var relativePath in manifest.Files)
        {
            var cases =
                JsonSerializer.Deserialize<IReadOnlyList<FixtureCase>>(
                    File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))),
                    JsonOptions
                ) ?? throw new InvalidOperationException($"Fixture file '{relativePath}' is invalid.");
            foreach (var fixture in cases)
                yield return [fixture];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Shared_fixture_contract_is_stable(FixtureCase fixture)
    {
        var engine = EngineTestFactory.Create();
        var definitionResult = engine.ParseDefinition(fixture.Definition.GetRawText());
        Assert.True(definitionResult.IsSuccess, $"{fixture.Name}: {definitionResult.Error.Code}");
        var definition = definitionResult.Value;

        IReadOnlyList<ValidationIssue> issues;
        VisibilityMap? visibility = null;
        FormProgress? progress = null;
        switch (fixture.Mode)
        {
            case "structure":
                issues = engine.ValidateForPublish(definition).Issues;
                break;
            case "draft":
            case "submit":
                var answersResult = engine.ParseAnswers(AnswersJson(fixture));
                Assert.True(answersResult.IsSuccess, $"{fixture.Name}: {answersResult.Error.Code}");
                issues = engine
                    .ValidateAnswers(
                        definition,
                        answersResult.Value,
                        fixture.Mode == "draft" ? AnswersValidationMode.Draft : AnswersValidationMode.Submit
                    )
                    .Issues;
                break;
            case "visibility":
                var visibilityAnswers = ParseAnswers(engine, fixture);
                visibility = engine.EvaluateVisibility(definition, visibilityAnswers);
                issues = engine.ValidateAnswers(definition, visibilityAnswers, AnswersValidationMode.Draft).Issues;
                break;
            case "progress":
                var progressAnswers = ParseAnswers(engine, fixture);
                visibility = engine.EvaluateVisibility(definition, progressAnswers);
                progress = engine.CalculateProgress(definition, progressAnswers, visibility);
                issues = engine.ValidateAnswers(definition, progressAnswers, AnswersValidationMode.Draft).Issues;
                break;
            case "mappings":
                issues = engine.ValidateMappings(definition, Catalog(fixture), Capabilities(fixture)).Issues;
                break;
            default:
                throw new InvalidOperationException($"Unknown fixture mode '{fixture.Mode}'.");
        }

        Assert.Equal(fixture.Expected.Valid, issues.Count == 0);
        Assert.Equal(
            fixture
                .Expected.Issues.Select(item => (item.Code, item.Path))
                .OrderBy(item => item.Code)
                .ThenBy(item => item.Path),
            issues
                .Select(item => (item.Code, item.Path))
                .Distinct()
                .OrderBy(item => item.Code)
                .ThenBy(item => item.Path)
        );
        if (fixture.Expected.Visible is not null)
            Assert.Equal(
                fixture.Expected.Visible.Order(StringComparer.Ordinal),
                visibility!.VisibleIds.Order(StringComparer.Ordinal)
            );
        if (fixture.Expected.Progress is not null)
        {
            Assert.Equal(fixture.Expected.Progress.Required, progress!.Required);
            Assert.Equal(fixture.Expected.Progress.Answered, progress.Answered);
        }
    }

    private static FormAnswers ParseAnswers(FormEngine engine, FixtureCase fixture)
    {
        var result = engine.ParseAnswers(AnswersJson(fixture));
        Assert.True(result.IsSuccess, $"{fixture.Name}: {result.Error.Code}");
        return result.Value;
    }

    private static string AnswersJson(FixtureCase fixture) =>
        fixture.Answers.ValueKind == JsonValueKind.Undefined
            ? "{\"schemaVersion\":1,\"values\":{}}"
            : fixture.Answers.GetRawText();

    private static MappingCatalogSnapshot Catalog(FixtureCase fixture) =>
        fixture.Catalog.Deserialize<MappingCatalogSnapshot>(CatalogOptions)
        ?? throw new InvalidOperationException($"{fixture.Name}: catalog is required.");

    private static PublisherCapabilities Capabilities(FixtureCase fixture)
    {
        var capabilities =
            fixture.Capabilities.Deserialize<FixtureCapabilities>(JsonOptions)
            ?? throw new InvalidOperationException($"{fixture.Name}: capabilities are required.");
        return new PublisherCapabilities(
            new HashSet<string>(capabilities.Permissions, StringComparer.Ordinal),
            capabilities.IsTenantAdmin
        );
    }

    private static string FixtureRoot() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1");

    public sealed record FixtureManifest(int FixtureSetVersion, IReadOnlyList<string> Files);

    public sealed record FixtureCase(
        string Name,
        string Mode,
        JsonElement Definition,
        JsonElement Answers,
        FixtureExpected Expected,
        JsonElement Catalog = default,
        JsonElement Capabilities = default
    );

    public sealed record FixtureCapabilities(IReadOnlyList<string> Permissions, bool IsTenantAdmin);

    public sealed record FixtureExpected(
        bool Valid,
        IReadOnlyList<FixtureIssue> Issues,
        IReadOnlyList<string>? Visible,
        FixtureProgress? Progress
    );

    public sealed record FixtureIssue(string Code, string Path);

    public sealed record FixtureProgress(int Required, int Answered);
}
