using TaxVision.Scribe.Application.Templates.BaseLayouts;
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
}
