using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Domain;

public class NoteContentTests
{
    [Fact]
    public void Create_WithValidHtml_Succeeds()
    {
        var result = NoteContent.Create("<p>Hello <b>world</b></p>");

        Assert.True(result.IsSuccess);
        Assert.Equal("<p>Hello <b>world</b></p>", result.Value.Html);
        Assert.Equal("Hello world", result.Value.PlainTextPreview);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyOrWhitespaceHtml_Fails(string? html)
    {
        var result = NoteContent.Create(html!);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.ContentEmpty, result.Error);
    }

    [Fact]
    public void Create_WithOnlyTagsAndNoText_FailsAsEmpty()
    {
        var result = NoteContent.Create("<p></p><br/>");

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.ContentEmpty, result.Error);
    }

    [Fact]
    public void Create_ExceedingMaxLength_Fails()
    {
        var oversized = new string('a', NoteContent.MaxHtmlLength + 1);

        var result = NoteContent.Create(oversized);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.ContentTooLong, result.Error);
    }

    [Fact]
    public void Create_PreviewIsTruncatedToPreviewLength()
    {
        var longText = new string('a', NoteContent.PreviewLength + 100);

        var result = NoteContent.Create($"<p>{longText}</p>");

        Assert.True(result.IsSuccess);
        Assert.Equal(NoteContent.PreviewLength, result.Value.PlainTextPreview.Length);
    }

    [Fact]
    public void Create_TrimsSurroundingWhitespace()
    {
        var result = NoteContent.Create("   <p>Hi</p>   ");

        Assert.True(result.IsSuccess);
        Assert.Equal("<p>Hi</p>", result.Value.Html);
    }
}
