using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application.Library;

public sealed class LibraryFilterTests
{
    [Fact]
    public void An_empty_request_uses_the_defaults()
    {
        var filter = Valid(new LibrarySearchRequest());

        Assert.Null(filter.Search);
        Assert.Empty(filter.Sources);
        Assert.Empty(filter.TypeGroups);
        Assert.Equal(LibrarySort.Newest, filter.Sort);
        Assert.Equal(50, filter.Take);
        Assert.Null(filter.Cursor);
        Assert.True(filter.IncludeCounts);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("  b  ")]
    [InlineData("   ")]
    public void A_search_shorter_than_two_characters_is_no_search(string q)
    {
        Assert.Null(Valid(new LibrarySearchRequest { Q = q }).Search);
    }

    [Fact]
    public void The_search_is_trimmed()
    {
        Assert.Equal("w-2", Valid(new LibrarySearchRequest { Q = "  w-2 " }).Search);
    }

    [Fact]
    public void A_search_longer_than_100_characters_is_invalid()
    {
        Invalid(new LibrarySearchRequest { Q = new string('x', 101) });
        Assert.NotNull(Valid(new LibrarySearchRequest { Q = new string('x', 100) }).Search);
    }

    [Theory]
    [InlineData("50%", @"%50\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData("[x]", @"%\[x]%")]
    [InlineData(@"c:\tmp", @"%c:\\tmp%")]
    public void Wildcards_in_the_search_are_escaped(string q, string pattern)
    {
        Assert.Equal(pattern, Valid(new LibrarySearchRequest { Q = q }).SearchPattern);
    }

    [Fact]
    public void Sources_are_parsed_and_deduplicated()
    {
        var filter = Valid(new LibrarySearchRequest { Source = ["billing", "documents", "billing"] });

        Assert.Equal([LibrarySource.Billing, LibrarySource.Documents], filter.Sources);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    public void An_unknown_source_is_invalid(string source)
    {
        Invalid(new LibrarySearchRequest { Source = [source] });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void Take_out_of_range_is_invalid(int take)
    {
        Invalid(new LibrarySearchRequest { Take = take });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Take_in_range_is_kept(int take)
    {
        Assert.Equal(take, Valid(new LibrarySearchRequest { Take = take }).Take);
    }

    [Theory]
    [InlineData(1989)]
    [InlineData(2101)]
    public void Tax_year_out_of_range_is_invalid(int year)
    {
        Invalid(new LibrarySearchRequest { TaxYear = year });
    }

    [Fact]
    public void Tax_year_in_range_is_kept()
    {
        Assert.Equal(2025, Valid(new LibrarySearchRequest { TaxYear = 2025 }).TaxYear);
    }

    [Theory]
    [InlineData("newest", LibrarySort.Newest)]
    [InlineData("oldest", LibrarySort.Oldest)]
    [InlineData("name", LibrarySort.Name)]
    public void Sort_is_parsed(string sort, LibrarySort expected)
    {
        Assert.Equal(expected, Valid(new LibrarySearchRequest { Sort = sort }).Sort);
    }

    [Fact]
    public void An_unknown_sort_is_invalid()
    {
        Invalid(new LibrarySearchRequest { Sort = "size" });
    }

    [Fact]
    public void An_unknown_type_group_is_invalid()
    {
        Invalid(new LibrarySearchRequest { TypeGroup = ["pdf", "zip"] });
    }

    [Fact]
    public void Type_groups_are_parsed()
    {
        var filter = Valid(new LibrarySearchRequest { TypeGroup = ["image", "other"] });

        Assert.Equal([LibraryTypeGroup.Image, LibraryTypeGroup.Other], filter.TypeGroups);
    }

    [Fact]
    public void A_range_whose_start_is_not_before_its_end_is_invalid()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 5, 0, 0, TimeSpan.Zero);

        Invalid(new LibrarySearchRequest { From = instant, To = instant });
        Invalid(new LibrarySearchRequest { From = instant.AddDays(1), To = instant });
    }

    [Fact]
    public void The_range_is_kept_in_utc()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-5));

        var filter = Valid(new LibrarySearchRequest { From = from, To = from.AddDays(1) });

        Assert.Equal(new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc), filter.FromUtc);
        Assert.Equal(DateTimeKind.Utc, filter.FromUtc!.Value.Kind);
    }

    [Fact]
    public void A_garbage_cursor_is_invalid()
    {
        Invalid(new LibrarySearchRequest { Cursor = "not-a-cursor" });
    }

    [Fact]
    public void A_cursor_turns_counts_off_by_default()
    {
        var cursor = new LibraryCursor(LibrarySort.Newest, "2026-01-01T00:00:00.0000000Z", Guid.NewGuid()).Encode();

        var filter = Valid(new LibrarySearchRequest { Cursor = cursor });

        Assert.False(filter.IncludeCounts);
        Assert.Equal(LibrarySort.Newest, filter.Cursor!.Sort);
        Assert.True(Valid(new LibrarySearchRequest { Cursor = cursor, Counts = true }).IncludeCounts);
    }

    [Fact]
    public void A_cursor_from_another_sort_is_invalid()
    {
        var cursor = new LibraryCursor(LibrarySort.Name, "a.pdf", Guid.NewGuid()).Encode();

        Invalid(new LibrarySearchRequest { Cursor = cursor, Sort = "newest" });
    }

    [Fact]
    public void Owner_filters_are_ignored_outside_the_documents_search()
    {
        var filter = Valid(new LibrarySearchRequest { OwnerType = "Customer", OwnerId = Guid.NewGuid() });

        Assert.Null(filter.OwnerType);
        Assert.Null(filter.OwnerId);
    }

    [Fact]
    public void The_documents_search_forces_its_source_and_takes_the_owner()
    {
        var ownerId = Guid.NewGuid();
        var request = new LibrarySearchRequest
        {
            Source = ["billing"],
            OwnerType = "Tenant",
            OwnerId = ownerId,
        };

        var filter = LibraryFilter.Create(request, LibrarySource.Documents).Value;

        Assert.Equal([LibrarySource.Documents], filter.Sources);
        Assert.Equal(OwnerType.Tenant, filter.OwnerType);
        Assert.Equal(ownerId, filter.OwnerId);
    }

    [Theory]
    [InlineData("Invoice")]
    [InlineData("User")]
    [InlineData("x")]
    public void The_documents_search_only_takes_customer_or_tenant_owners(string ownerType)
    {
        var result = LibraryFilter.Create(new LibrarySearchRequest { OwnerType = ownerType }, LibrarySource.Documents);

        Assert.Equal(LibraryErrors.InvalidQueryCode, result.Error.Code);
    }

    [Theory]
    [InlineData("application/pdf", LibraryTypeGroup.Pdf)]
    [InlineData("image/svg+xml", LibraryTypeGroup.Image)]
    [InlineData("text/csv", LibraryTypeGroup.Spreadsheet)]
    [InlineData("application/rtf", LibraryTypeGroup.Document)]
    [InlineData("video/mp4", LibraryTypeGroup.Media)]
    [InlineData("audio/mp4", LibraryTypeGroup.Media)]
    [InlineData("application/zip", LibraryTypeGroup.Other)]
    public void Each_content_type_falls_in_one_group(string contentType, LibraryTypeGroup expected)
    {
        Assert.Equal(expected, LibraryTypeGroups.Of(contentType));
    }

    private static LibraryFilter Valid(LibrarySearchRequest request)
    {
        var result = LibraryFilter.Create(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static void Invalid(LibrarySearchRequest request)
    {
        var result = LibraryFilter.Create(request);
        Assert.True(result.IsFailure);
        Assert.Equal(LibraryErrors.InvalidQueryCode, result.Error.Code);
    }
}
