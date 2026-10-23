using System.Text.Json;
using TaxVision.DataCollection.Engine.Serialization;
using TaxVision.DataCollection.Engine.Text;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class RichTextTests
{
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("http://example.com")]
    public void Rejects_non_https_links(string href)
    {
        var richText = new
        {
            type = "doc",
            blocks = new[]
            {
                new
                {
                    type = "paragraph",
                    children = new[]
                    {
                        new
                        {
                            type = "link",
                            href,
                            children = new[] { new { type = "text", text = "Link" } },
                        },
                    },
                },
            },
        };
        var report = EngineTestFactory
            .Create()
            .ValidateForPublish(EngineTestFactory.Definition("paragraph", new { text = richText }));

        Assert.Contains(report.Issues, issue => issue.Code == "richtext.invalid_link");
    }

    [Fact]
    public void Rejects_unknown_rich_text_nodes()
    {
        var richText = new
        {
            type = "doc",
            blocks = new[] { new { type = "image", children = Array.Empty<object>() } },
        };
        var report = EngineTestFactory
            .Create()
            .ValidateForPublish(EngineTestFactory.Definition("paragraph", new { text = richText }));

        Assert.Contains(report.Issues, issue => issue.Code == "richtext.invalid_node");
    }

    [Theory]
    [InlineData(
        "{\"type\":\"paragraph\",\"children\":[{\"type\":\"link\",\"href\":\"javascript:alert(1)\",\"children\":[{\"type\":\"text\",\"text\":\"Sign\"}]}]}",
        "richtext.invalid_link"
    )]
    [InlineData(
        "{\"type\":\"paragraph\",\"children\":[{\"type\":\"link\",\"href\":\"data:text/html,x\",\"children\":[{\"type\":\"text\",\"text\":\"Sign\"}]}]}",
        "richtext.invalid_link"
    )]
    [InlineData("{\"type\":\"table\",\"children\":[{\"type\":\"text\",\"text\":\"Cell\"}]}", "richtext.invalid_node")]
    public void Signature_step_instructions_are_validated_as_rich_text(string block, string expectedCode)
    {
        var props = JsonSerializer.Deserialize<JsonElement>(
            $$$"""{"documentSource":"submissionSummary","verification":"emailOtp","instructions":{"type":"doc","blocks":[{{{block}}}]}}"""
        );

        var report = EngineTestFactory
            .Create()
            .ValidateForPublish(EngineTestFactory.Definition("signatureStep", props));

        Assert.Contains(
            report.Issues,
            issue => issue.Code == expectedCode && issue.Path.Contains("/props/instructions/", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Html_like_text_is_literal_content()
    {
        var richText = new
        {
            type = "doc",
            blocks = new[]
            {
                new { type = "paragraph", children = new[] { new { type = "text", text = "<img onerror=alert(1)>" } } },
            },
        };
        var report = EngineTestFactory
            .Create()
            .ValidateForPublish(EngineTestFactory.Definition("paragraph", new { text = richText }));

        Assert.DoesNotContain(report.Issues, issue => issue.Code.StartsWith("richtext.", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_list_children_outside_items()
    {
        var richText = new
        {
            type = "doc",
            blocks = new[]
            {
                new
                {
                    type = "bulletList",
                    children = new[] { new { type = "text", text = "Invalid" } },
                    items = new[] { new[] { new { type = "text", text = "Item" } } },
                },
            },
        };

        var report = EngineTestFactory
            .Create()
            .ValidateForPublish(EngineTestFactory.Definition("paragraph", new { text = richText }));

        Assert.Contains(report.Issues, issue => issue.Code == "richtext.invalid_node");
    }

    [Fact]
    public void Rejects_nested_links()
    {
        var richText = new
        {
            type = "doc",
            blocks = new[]
            {
                new
                {
                    type = "paragraph",
                    children = new[]
                    {
                        new
                        {
                            type = "link",
                            href = "https://example.com",
                            children = new[]
                            {
                                new
                                {
                                    type = "link",
                                    href = "https://example.com/child",
                                    children = new[] { new { type = "text", text = "Nested" } },
                                },
                            },
                        },
                    },
                },
            },
        };

        var report = EngineTestFactory
            .Create()
            .ValidateForPublish(EngineTestFactory.Definition("paragraph", new { text = richText }));

        Assert.Contains(report.Issues, issue => issue.Code == "richtext.invalid_node");
    }

    [Fact]
    public void Canonicalizer_is_stable_for_equivalent_property_order()
    {
        var first = JsonSerializer.Deserialize<RichTextDocument>(
            "{\"type\":\"doc\",\"blocks\":[{\"type\":\"paragraph\",\"align\":\"left\",\"children\":[{\"type\":\"text\",\"text\":\"Hello\"}]}]}",
            StrictJson.Options
        )!;
        var second = JsonSerializer.Deserialize<RichTextDocument>(
            "{\"blocks\":[{\"children\":[{\"text\":\"Hello\",\"type\":\"text\"}],\"align\":\"left\",\"type\":\"paragraph\"}],\"type\":\"doc\"}",
            StrictJson.Options
        )!;

        Assert.Equal(RichTextCanonicalizer.Canonicalize(first), RichTextCanonicalizer.Canonicalize(second));
    }
}
