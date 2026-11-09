using TaxVision.Scribe.Application.Templates.Seed;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Tests.Templates.Seed;

/// <summary>SCR-01: la huella del contenido es lo que decide republicar, no un número puesto a mano.</summary>
public sealed class NotificationTemplateSeedTests
{
    private const string WispKey = "tenant.wisp_review_due.v1";

    private static NotificationTemplateSeed Seed =>
        new(
            EventKey: "sample.event",
            TemplateKey: "sample.template",
            Name: "Sample",
            Subject: "Subject",
            Html: "<p>Hello</p>",
            Variables:
            [
                ("first", VariableType.String, true, null, "First."),
                ("second", VariableType.Bool, false, "false", "Second."),
            ],
            Text: "Hello"
        );

    [Fact]
    public void Content_hash_changes_when_any_part_changes()
    {
        var variants = new[]
        {
            Seed with { Subject = "Other" },
            Seed with { Html = "<p>Hello.</p>" },
            Seed with { Text = "Hello." },
            Seed with { Variables = [("first", VariableType.String, true, null, "First.")] },
            Seed with
            {
                Variables =
                [
                    ("first", VariableType.String, true, "default", "First."),
                    ("second", VariableType.Bool, false, "false", "Second."),
                ],
            },
            Seed with { LayoutKey = "tenant-base" },
            Seed with { Channel = TemplateChannel.Push },
        };

        Assert.All(variants, variant => Assert.NotEqual(Seed.ContentHash, variant.ContentHash));
    }

    [Fact]
    public void Content_hash_ignores_variable_order()
    {
        var reordered = Seed with { Variables = [.. Seed.Variables.Reverse()] };

        Assert.Equal(Seed.ContentHash, reordered.ContentHash);
    }

    [Fact]
    public void Content_hash_ignores_the_line_endings_of_the_build_machine()
    {
        var unix = Seed with { Html = "<p>\nHello\n</p>" };
        var windows = Seed with { Html = "<p>\r\nHello\r\n</p>" };

        Assert.Equal(unix.ContentHash, windows.ContentHash);
    }

    // El preheader se guarda en la versión pero vive en otro diccionario: sin esto, cambiarlo no republicaría.
    [Fact]
    public void Content_hash_covers_the_preheader()
    {
        var withPreheader = NotificationTemplateSeedSource.All.Single(seed => seed.TemplateKey == WispKey);
        var withoutPreheader = withPreheader with { TemplateKey = "sample.no_preheader" };

        Assert.NotEqual(withPreheader.ContentHash, withoutPreheader.ContentHash);
    }

    [Fact]
    public void Content_version_is_only_a_label()
    {
        Assert.Equal(Seed.ContentHash, (Seed with { ContentVersion = 99 }).ContentHash);
    }

    [Fact]
    public void Content_hash_fits_the_column()
    {
        Assert.Matches("^[0-9a-f]{64}$", Seed.ContentHash);
    }

    [Fact]
    public void A_template_that_declares_no_version_uses_1()
    {
        Assert.Equal(1, Seed.ContentVersion);
    }
}
