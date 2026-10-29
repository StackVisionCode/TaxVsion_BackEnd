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
        var optionalPart =
            TooLong(middleName, MiddleNameMaxLength, "Middle name")
            ?? TooLong(prefix, PrefixMaxLength, "Prefix")
            ?? TooLong(suffix, SuffixMaxLength, "Suffix");
        if (optionalPart is not null)
            return Result.Failure<PersonalName>(optionalPart);
        return Result.Success(new PersonalName(prefix, firstName, middleName, lastName, suffix));
    }

    // Mismos límites que las columnas (CustomerConfiguration y CustomerRelationConfiguration).
    public const int MiddleNameMaxLength = 80;
    public const int PrefixMaxLength = 20;
    public const int SuffixMaxLength = 20;

    // Se mide el valor recortado: es el que se guarda.
    private static Error? TooLong(string? value, int maxLength, string label) =>
        value is not null && value.Trim().Length > maxLength
            ? new Error("PersonalName.Length", $"{label} exceeds maximum length of {maxLength} characters.")
            : null;
}
