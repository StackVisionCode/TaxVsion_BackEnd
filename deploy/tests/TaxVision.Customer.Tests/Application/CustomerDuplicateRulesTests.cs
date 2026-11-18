using TaxVision.Customer.Application.Customers;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// El nombre de una persona casa por nombre y apellido; el segundo nombre solo separa si los dos lo traen y no son
/// compatibles ("Ali" y "A." sí; "Ali" y "Bo" no). Así "Omar Ali Haddad" es "Omar Haddad", y "John A. Smith" y
/// "John B. Smith" con el teléfono de la casa siguen siendo dos personas.
/// </summary>
public sealed class CustomerDuplicateRulesTests
{
    private const string Phone = "+13055550104";
    private static readonly DateOnly Dob = new(1990, 11, 5);

    [Theory]
    [InlineData(null, "ali")]
    [InlineData("ali", null)]
    [InlineData("ali", "ali")]
    [InlineData("ali", "a.")]
    [InlineData("a", "ali")]
    public void A_compatible_middle_name_is_the_same_person(string? existingMiddle, string? fileMiddle)
    {
        var existing = Existing("omar haddad", existingMiddle);

        var hit = CustomerDuplicateRules.Match(Keys("omar haddad", fileMiddle, phone: Phone), [existing]);

        Assert.Equal(CustomerDuplicateRules.ByPhoneAndName, hit?.MatchedBy);
    }

    [Fact]
    public void Different_middle_names_are_different_people()
    {
        var father = Existing("john smith", "a.");

        Assert.Null(CustomerDuplicateRules.Match(Keys("john smith", "b.", phone: Phone), [father]));
        Assert.Null(CustomerDuplicateRules.Match(Keys("john smith", "bob", dob: Dob), [father]));
    }

    [Fact]
    public void Name_plus_date_of_birth_ignores_a_missing_middle_name()
    {
        var hit = CustomerDuplicateRules.Match(Keys("omar haddad", "ali", dob: Dob), [Existing("omar haddad", null)]);

        Assert.Equal(CustomerDuplicateRules.ByNameAndDateOfBirth, hit?.MatchedBy);
    }

    [Fact]
    public void A_different_first_or_last_name_never_matches()
    {
        Assert.Null(
            CustomerDuplicateRules.Match(Keys("omar hadad", null, phone: Phone), [Existing("omar haddad", null)])
        );
    }

    private static CustomerDuplicateRules.ExistingCustomer Existing(string name, string? middle) =>
        new(Guid.NewGuid(), name, name, middle, Dob, null, Phone);

    private static CustomerDuplicateRules.Keys Keys(
        string name,
        string? middle,
        string? phone = null,
        DateOnly? dob = null
    ) => new(null, phone, name, dob, middle);
}
