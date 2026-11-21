using System.Text;
using TaxVision.CloudStorage.Application.Library;

namespace TaxVision.CloudStorage.Tests.Application.Library;

public sealed class LibraryCursorTests
{
    [Theory]
    [InlineData(LibrarySort.Newest, "2026-11-18T20:42:11.1234567Z")]
    [InlineData(LibrarySort.Oldest, "2020-01-01T00:00:00.0000000Z")]
    [InlineData(LibrarySort.Name, "W-2 2025 (copia) ñ.pdf")]
    public void A_cursor_round_trips(LibrarySort sort, string key)
    {
        var cursor = new LibraryCursor(sort, key, Guid.NewGuid());

        var decoded = LibraryCursor.Decode(cursor.Encode(), sort);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(cursor, decoded.Value);
    }

    [Fact]
    public void The_encoded_cursor_is_url_safe()
    {
        var encoded = new LibraryCursor(LibrarySort.Name, "??>>~~", Guid.NewGuid()).Encode();

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Fact]
    public void A_cursor_after_a_row_keeps_its_date_to_the_tick()
    {
        var createdAt = new DateTime(2026, 11, 18, 20, 42, 11, DateTimeKind.Utc).AddTicks(1234567);
        var id = Guid.NewGuid();

        var cursor = LibraryCursor.After(LibrarySort.Newest, createdAt, "x.pdf", id);

        Assert.Equal(createdAt, cursor.CreatedAtKey);
        Assert.Equal(DateTimeKind.Utc, cursor.CreatedAtKey.Kind);
        Assert.Equal(id, cursor.Id);
    }

    [Fact]
    public void A_cursor_after_a_row_sorted_by_name_keeps_the_name()
    {
        var cursor = LibraryCursor.After(LibrarySort.Name, DateTime.UtcNow, "b.pdf", Guid.NewGuid());

        Assert.Equal("b.pdf", cursor.Key);
    }

    [Fact]
    public void A_cursor_of_another_sort_is_rejected()
    {
        var encoded = new LibraryCursor(LibrarySort.Oldest, "2026-01-01T00:00:00.0000000Z", Guid.NewGuid()).Encode();

        Assert.Equal(LibraryErrors.InvalidQueryCode, LibraryCursor.Decode(encoded, LibrarySort.Newest).Error.Code);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("eyJ9")]
    [InlineData("")]
    public void Garbage_is_rejected(string value)
    {
        Assert.Equal(LibraryErrors.InvalidQueryCode, LibraryCursor.Decode(value, LibrarySort.Newest).Error.Code);
    }

    [Theory]
    [InlineData("{\"s\":\"newest\",\"k\":\"yesterday\",\"id\":\"0b6c2f1e-5a0d-4a51-9d0e-0a1b2c3d4e5f\"}")]
    [InlineData("{\"s\":\"newest\",\"k\":\"2026-01-01T00:00:00Z\",\"id\":\"nope\"}")]
    [InlineData("{\"s\":\"newest\",\"id\":\"0b6c2f1e-5a0d-4a51-9d0e-0a1b2c3d4e5f\"}")]
    public void A_well_formed_cursor_with_a_bad_key_is_rejected(string json)
    {
        var encoded = Convert
            .ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.True(LibraryCursor.Decode(encoded, LibrarySort.Newest).IsFailure);
    }
}
