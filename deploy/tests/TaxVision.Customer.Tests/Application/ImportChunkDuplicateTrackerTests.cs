using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// B-17: dos filas del mismo archivo son duplicadas solo si la API rechazaría la segunda con la primera ya creada
/// (más el identificador fiscal, que solo trae el import). Antes bastaba el teléfono.
/// </summary>
public sealed class ImportChunkDuplicateTrackerTests
{
    private static readonly DateOnly Dob = new(1990, 12, 10);

    [Fact]
    public void Two_people_sharing_a_phone_are_not_duplicates()
    {
        var tracker = new ImportChunkDuplicateTracker();

        Assert.Null(tracker.TryAdd(Keys("ada@x.test", "+13055550123", "ada lovelace"), null));
        Assert.Null(tracker.TryAdd(Keys("grace@x.test", "+13055550123", "grace hopper"), null));
    }

    [Fact]
    public void Same_phone_and_name_is_a_duplicate()
    {
        var tracker = new ImportChunkDuplicateTracker();

        tracker.TryAdd(Keys("ada@x.test", "+13055550123", "ada lovelace"), null);

        Assert.Equal(
            "Another row in the same chunk has the same phone and name.",
            tracker.TryAdd(Keys("other@x.test", "+13055550123", "ada lovelace"), null)
        );
    }

    [Fact]
    public void Same_email_is_a_duplicate()
    {
        var tracker = new ImportChunkDuplicateTracker();

        tracker.TryAdd(Keys("ada@x.test", null, "ada lovelace"), null);

        Assert.Equal(
            "Another row in the same chunk has the same email.",
            tracker.TryAdd(Keys("ada@x.test", null, "someone else"), null)
        );
    }

    [Fact]
    public void Same_name_and_date_of_birth_is_a_duplicate()
    {
        var tracker = new ImportChunkDuplicateTracker();

        tracker.TryAdd(Keys("ada@x.test", null, "ada lovelace", Dob), null);

        Assert.Equal(
            "Another row in the same chunk has the same name + date of birth.",
            tracker.TryAdd(Keys("other@x.test", null, "ada lovelace", Dob), null)
        );
    }

    [Fact]
    public void Same_tax_identifier_is_a_duplicate()
    {
        var tracker = new ImportChunkDuplicateTracker();

        tracker.TryAdd(Keys("ada@x.test", null, "ada lovelace"), "bi-1");

        Assert.Equal(
            "Another row in the same chunk has the same tax identifier.",
            tracker.TryAdd(Keys("other@x.test", null, "grace hopper"), "bi-1")
        );
    }

    [Fact]
    public void A_rejected_row_does_not_block_the_rows_after_it()
    {
        var tracker = new ImportChunkDuplicateTracker();
        tracker.TryAdd(Keys("ada@x.test", null, "ada lovelace"), null);

        // Rechazada por email: su teléfono + nombre no quedan registrados.
        Assert.NotNull(tracker.TryAdd(Keys("ada@x.test", "+13055550123", "grace hopper"), null));

        Assert.Null(tracker.TryAdd(Keys("grace@x.test", "+13055550123", "grace hopper"), null));
    }

    [Fact]
    public void Keys_from_a_raw_row_match_what_the_api_compares()
    {
        var keys = ImportDuplicateKeys.From(
            new ImportCustomerRow
            {
                RowNumber = 1,
                Kind = "Individual",
                FirstName = " Ada ",
                MiddleName = "Maria",
                LastName = "Lovelace",
                Prefix = "Dr.",
                Email = " ADA@Example.TEST ",
                Phone = "(305) 555-0123",
                DateOfBirth = "1990-12-10",
            }
        );

        Assert.Equal(
            new CustomerDuplicateRules.Keys("ada@example.test", "+13055550123", "ada maria lovelace", Dob),
            keys
        );
    }

    private static CustomerDuplicateRules.Keys Keys(string? email, string? phone, string? name, DateOnly? dob = null) =>
        new(email, phone, name, dob);
}
