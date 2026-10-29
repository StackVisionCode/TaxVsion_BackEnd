using TaxVision.Notes.Infrastructure.Notes;

namespace TaxVision.Notes.Tests.Infrastructure;

public sealed class GanssHtmlSanitizerTests
{
    private readonly GanssHtmlSanitizer _sanitizer = new();

    // Lo que emite hoy el CRM: texto escapado + <br> (perfil de cliente) y <p> (dashboard).
    [Theory]
    [InlineData("Call the client tomorrow")]
    [InlineData("Line one<br>Line two<br>Line three")]
    [InlineData("1 &lt; 2 &amp;&amp; 3 &gt; 2<br>&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("<p>Dashboard note</p>")]
    [InlineData("<p>&lt;b&gt;not bold&lt;/b&gt;</p>")]
    // Formato enriquecido que la allowlist mantiene.
    [InlineData("<p>Hello <b>bold</b> <strong>strong</strong> <i>italic</i> <em>em</em> <u>under</u> <s>gone</s></p>")]
    [InlineData("<h1>Title</h1><h2>Sub</h2><h3>Third</h3>")]
    [InlineData("<ul><li>one</li><li>two</li></ul><ol><li>first</li></ol>")]
    [InlineData("<blockquote>quoted</blockquote><pre><code>var x = 1;</code></pre><hr>")]
    [InlineData("<p><a href=\"https://example.com/path?q=1\">link</a></p>")]
    [InlineData("<p><a href=\"mailto:ana@example.com\">mail</a></p>")]
    [InlineData("<a href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">new tab</a>")]
    public void Legitimate_editor_html_round_trips_unchanged(string html)
    {
        Assert.Equal(html, _sanitizer.Sanitize(html));
    }

    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<p onclick=\"alert(1)\">click</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">x</a>")]
    [InlineData("<a href=\"java&#09;script:alert(1)\">x</a>")]
    [InlineData("<a href=\"&#106;avascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">x</a>")]
    [InlineData("<svg onload=alert(1)><circle r=\"1\"></circle></svg>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>")]
    [InlineData("<p style=\"width:expression(alert(1))\">x</p>")]
    [InlineData("<p style=\"background:url(javascript:alert(1))\">x</p>")]
    [InlineData("<style>p{background:url(javascript:alert(1))}</style>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<object data=\"https://evil.example/x.swf\"></object>")]
    [InlineData("<embed src=\"https://evil.example/x.swf\">")]
    [InlineData("<form action=\"https://evil.example\"><input name=\"p\"></form>")]
    [InlineData("<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>")]
    [InlineData("<p data-user=\"1\" class=\"x\" id=\"y\">x</p>")]
    public void Dangerous_payloads_are_neutralized(string payload)
    {
        var sanitized = _sanitizer.Sanitize(payload);

        Assert.DoesNotContain("<script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<object", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<embed", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<form", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expression(", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsafe_link_keeps_its_text_but_loses_the_href()
    {
        Assert.Equal("<a>x</a>", _sanitizer.Sanitize("<a href=\"javascript:alert(1)\">x</a>"));
    }

    [Fact]
    public void Event_handler_is_stripped_but_the_paragraph_survives()
    {
        Assert.Equal("<p>click</p>", _sanitizer.Sanitize("<p onclick=\"alert(1)\">click</p>"));
    }

    [Fact]
    public void Target_blank_always_gets_noopener_noreferrer()
    {
        var sanitized = _sanitizer.Sanitize("<a href=\"https://example.com\" target=\"_BLANK\" rel=\"opener\">x</a>");

        Assert.Equal("<a href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">x</a>", sanitized);
    }

    [Fact]
    public void Target_blank_without_rel_gets_noopener_noreferrer()
    {
        var sanitized = _sanitizer.Sanitize("<a href=\"https://example.com\" target=\"_blank\">x</a>");

        Assert.Equal("<a href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">x</a>", sanitized);
    }

    [Theory]
    [InlineData("_top")]
    [InlineData("_parent")]
    [InlineData("some-frame")]
    public void Targets_other_than_blank_are_dropped(string target)
    {
        var sanitized = _sanitizer.Sanitize($"<a href=\"https://example.com\" target=\"{target}\">x</a>");

        Assert.Equal("<a href=\"https://example.com\">x</a>", sanitized);
    }
}
