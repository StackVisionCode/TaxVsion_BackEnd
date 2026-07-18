using TaxVision.Correspondence.Application.Ingest;

namespace TaxVision.Correspondence.Tests.Ingest;

public sealed class SubjectNormalizerTests
{
    [Theory]
    [InlineData("Re: Re: Fwd: Hello", "  Hello  ")]
    [InlineData("RE: Hello", "hello")]
    [InlineData("Fwd: FW: Re: Hello", "Hello")]
    public void Normalize_strips_repeated_reply_forward_prefixes_and_whitespace_so_equivalent_subjects_match(
        string subjectA,
        string subjectB
    )
    {
        Assert.Equal(SubjectNormalizer.Normalize(subjectA), SubjectNormalizer.Normalize(subjectB));
    }

    [Fact]
    public void Normalize_does_not_treat_subjects_with_different_content_as_equal()
    {
        Assert.NotEqual(SubjectNormalizer.Normalize("Re: Hello"), SubjectNormalizer.Normalize("Re: Hello 2"));
    }

    [Fact]
    public void Normalize_collapses_internal_whitespace()
    {
        Assert.Equal("hello world", SubjectNormalizer.Normalize("Hello   world"));
    }
}
