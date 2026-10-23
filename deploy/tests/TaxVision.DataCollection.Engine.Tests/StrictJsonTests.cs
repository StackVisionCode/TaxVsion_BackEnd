using TaxVision.DataCollection.Engine;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class StrictJsonTests
{
    [Fact]
    public void Parses_a_valid_definition()
    {
        var result = EngineTestFactory.Create().ParseDefinition(ValidJson());

        Assert.True(result.IsSuccess);
        Assert.Equal("Client intake", result.Value.Title);
    }

    [Theory]
    [InlineData("unknown", "true")]
    [InlineData("title", "\"Duplicate\"")]
    public void Rejects_unknown_or_duplicate_root_properties(string property, string value)
    {
        var json = ValidJson().Replace("\"pages\":", $"\"{property}\":{value},\"pages\":", StringComparison.Ordinal);
        var result = EngineTestFactory.Create().ParseDefinition(json);

        Assert.True(result.IsFailure);
        Assert.Equal("definition.invalid_json", result.Error.Code);
    }

    [Fact]
    public void Rejects_trailing_commas()
    {
        var result = EngineTestFactory
            .Create()
            .ParseDefinition(ValidJson().Replace("}]}", "}],}", StringComparison.Ordinal));

        Assert.Equal("definition.invalid_json", result.Error.Code);
    }

    [Fact]
    public void Rejects_payload_before_parsing_when_it_is_too_large()
    {
        var engine = EngineTestFactory.Create(new DataCollectionLimits { MaxDefinitionBytes = 1_024 });
        var result = engine.ParseDefinition(new string('x', 1_025));

        Assert.Equal("definition.too_large", result.Error.Code);
    }

    [Fact]
    public void Rejects_default_limit_payload_of_600_kilobytes()
    {
        var result = EngineTestFactory.Create().ParseDefinition(new string('x', 600 * 1024));

        Assert.Equal("definition.too_large", result.Error.Code);
    }

    [Fact]
    public void Rejects_json_deeper_than_32_levels()
    {
        var nested = string.Concat(Enumerable.Repeat("{\"a\":", 40)) + "1" + string.Concat(Enumerable.Repeat("}", 40));
        var result = EngineTestFactory.Create().ParseDefinition(nested);

        Assert.Equal("definition.invalid_json", result.Error.Code);
    }

    [Fact]
    public void Rejects_unknown_answer_properties()
    {
        var result = EngineTestFactory.Create().ParseAnswers("{\"schemaVersion\":1,\"values\":{},\"unknown\":true}");

        Assert.Equal("answers.invalid_json", result.Error.Code);
    }

    [Theory]
    [InlineData("\"title\":\"Client intake\",")]
    [InlineData("\"purpose\":\"clientIntake\",")]
    [InlineData("\"presentation\":\"wizard\",")]
    [InlineData(
        "\"pages\":[{\"id\":\"pg_abcdefgh\",\"title\":\"Page\",\"description\":null,\"visibleWhen\":null,\"style\":null,\"sections\":[{\"id\":\"sc_abcdefgh\",\"title\":\"Section\",\"description\":null,\"visibleWhen\":null,\"repeat\":null,\"style\":null,\"components\":[{\"id\":\"fd_abcdefgh\",\"type\":\"text\",\"key\":null,\"label\":\"Name\",\"help\":null,\"required\":false,\"requiredWhen\":null,\"visibleWhen\":null,\"dataClass\":\"personal\",\"reportable\":false,\"prefill\":null,\"style\":null,\"props\":{}}]}]}],"
    )]
    public void Rejects_missing_required_definition_members(string member)
    {
        var result = EngineTestFactory
            .Create()
            .ParseDefinition(ValidJson().Replace(member, string.Empty, StringComparison.Ordinal));

        Assert.Equal("definition.invalid_json", result.Error.Code);
    }

    [Fact]
    public void Rejects_missing_answers_values()
    {
        var result = EngineTestFactory.Create().ParseAnswers("{\"schemaVersion\":1}");

        Assert.Equal("answers.invalid_json", result.Error.Code);
    }

    private static string ValidJson() =>
        """
            {"schemaVersion":1,"title":"Client intake","purpose":"clientIntake","presentation":"wizard","settings":{"showProgress":true},"pages":[{"id":"pg_abcdefgh","title":"Page","description":null,"visibleWhen":null,"style":null,"sections":[{"id":"sc_abcdefgh","title":"Section","description":null,"visibleWhen":null,"repeat":null,"style":null,"components":[{"id":"fd_abcdefgh","type":"text","key":null,"label":"Name","help":null,"required":false,"requiredWhen":null,"visibleWhen":null,"dataClass":"personal","reportable":false,"prefill":null,"style":null,"props":{}}]}]}],"mappings":[],"actions":[]}
            """;
}
