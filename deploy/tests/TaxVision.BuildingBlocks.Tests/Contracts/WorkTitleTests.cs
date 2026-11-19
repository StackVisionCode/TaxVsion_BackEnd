using BuildingBlocks.Contracts.Work;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Contracts;

/// <summary>Títulos del historial: una línea, sin espacios de sobra y con tope de largo.</summary>
public sealed class WorkTitleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void Empty_title_is_null(string? title) => Assert.Null(WorkLimits.ClipTitle(title));

    [Fact]
    public void Collapses_whitespace_and_line_breaks()
    {
        Assert.Equal("Review W-2 for Ana", WorkLimits.ClipTitle("  Review\r\n W-2   for\tAna "));
    }

    [Fact]
    public void Long_title_is_clipped_with_an_ellipsis()
    {
        var clipped = WorkLimits.ClipTitle(new string('a', 300))!;

        Assert.Equal(WorkLimits.TitleMaxLength, clipped.Length);
        Assert.EndsWith("…", clipped);
    }

    [Fact]
    public void Title_at_the_limit_is_kept_whole()
    {
        var exact = new string('b', WorkLimits.TitleMaxLength);

        Assert.Equal(exact, WorkLimits.ClipTitle(exact));
    }
}
