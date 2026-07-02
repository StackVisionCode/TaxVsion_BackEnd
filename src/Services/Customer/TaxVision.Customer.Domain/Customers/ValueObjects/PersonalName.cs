using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Customers.ValueObjects;

public sealed record PersonalName
{
    public string? Prefix { get; }
    public string FirstName { get; }
    public string? MiddleName { get; }
    public string LastName { get; }
    public string? Suffix { get; }

    public string DisplayName =>
        string.Join(' ', new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private PersonalName(string? prefix, string firstName, string? middleName, string lastName, string? suffix)
    {
        Prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim();
        FirstName = firstName.Trim();
        MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName.Trim();
        LastName = lastName.Trim();
        Suffix = string.IsNullOrWhiteSpace(suffix) ? null : suffix.Trim();
    }

    public static Result<PersonalName> Create(
        string firstName,
        string lastName,
        string? middleName = null,
        string? prefix = null,
        string? suffix = null
    )
    {
        if (string.IsNullOrWhiteSpace(firstName))
            return Result.Failure<PersonalName>(new Error("PersonalName.FirstName", "First name is required."));
        if (string.IsNullOrWhiteSpace(lastName))
            return Result.Failure<PersonalName>(new Error("PersonalName.LastName", "Last name is required."));
        if (firstName.Length > 80 || lastName.Length > 80)
            return Result.Failure<PersonalName>(new Error("PersonalName.Length", "Name exceeds maximum length."));
        return Result.Success(new PersonalName(prefix, firstName, middleName, lastName, suffix));
    }
}
